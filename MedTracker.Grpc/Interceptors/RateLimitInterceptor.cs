using Grpc.Core;
using Grpc.Core.Interceptors;
using StackExchange.Redis;

namespace MedTracker.Grpc.Interceptors;

/// <summary>
/// Distributed sliding-window rate limiter per IP per method, backed by Redis.
/// Sorted set + атомарный Lua-скрипт обеспечивают согласованность счётчиков
/// между всеми репликами сервиса.
///
/// IP клиента берётся из заголовков X-Real-IP / X-Forwarded-For, которые
/// проставляет nginx (см. nginx.conf). Только если их нет — fallback на
/// context.Peer (что покажет IP nginx-контейнера, не клиента).
/// </summary>
public class RateLimitInterceptor : Interceptor
{
    private static readonly Dictionary<string, RateLimitRule> _rules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/medtracker.AuthService/Login"] = new(5, TimeSpan.FromMinutes(1)),
        ["/medtracker.AuthService/Register"] = new(3, TimeSpan.FromHours(1)),
        ["/medtracker.AuthService/RefreshToken"] = new(10, TimeSpan.FromMinutes(1))
    };

    // Lua: атомарно убирает старые записи, считает текущие, добавляет новую если есть лимит.
    // KEYS[1] = ключ, ARGV[1] = now (ms), ARGV[2] = windowStart (ms), ARGV[3] = max, ARGV[4] = TTL (s)
    private const string SlidingWindowScript = @"
redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[2])
local count = redis.call('ZCARD', KEYS[1])
if tonumber(count) >= tonumber(ARGV[3]) then
  return 0
end
redis.call('ZADD', KEYS[1], ARGV[1], ARGV[1] .. ':' .. math.random())
redis.call('EXPIRE', KEYS[1], ARGV[4])
return 1
";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RateLimitInterceptor> _logger;

    public RateLimitInterceptor(IConnectionMultiplexer redis, ILogger<RateLimitInterceptor> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        await CheckRateLimitAsync(context);
        return await continuation(request, context);
    }

    private async Task CheckRateLimitAsync(ServerCallContext context)
    {
        if (!_rules.TryGetValue(context.Method, out var rule))
            return;

        var clientIp = ResolveClientIp(context);
        var key = $"rl:{clientIp}:{context.Method}";
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowStartMs = nowMs - (long)rule.Window.TotalMilliseconds;
        var ttlSec = (long)Math.Ceiling(rule.Window.TotalSeconds) + 1;

        try
        {
            var db = _redis.GetDatabase();
            var result = await db.ScriptEvaluateAsync(
                SlidingWindowScript,
                keys: new RedisKey[] { key },
                values: new RedisValue[] { nowMs, windowStartMs, rule.MaxRequests, ttlSec });

            if ((int)result == 0)
            {
                _logger.LogWarning("Rate limit exceeded for {ClientIp} on {Method}", clientIp, context.Method);
                throw new RpcException(new Status(
                    StatusCode.ResourceExhausted,
                    $"Rate limit exceeded. Max {rule.MaxRequests} requests per {rule.Window.TotalSeconds:0}s."));
            }
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fail-open: Redis недоступен — пропускаем. Альтернатива — кинуть Unavailable.
            // Для auth-методов fail-closed безопаснее (атакующий не может уронить Redis и снять лимиты).
            _logger.LogError(ex, "Rate limiter unavailable, allowing request through");
        }
    }

    /// <summary>
    /// Достаёт реальный IP клиента, отдавая предпочтение заголовкам от reverse proxy:
    ///   1. X-Real-IP — единичный IP, выставляется nginx (grpc_set_header X-Real-IP $remote_addr)
    ///   2. X-Forwarded-For — цепочка прокси, берём ПЕРВЫЙ (исходный клиент)
    ///   3. context.Peer — fallback, формат "ipv4:172.18.0.5:54321", отрезаем порт
    ///
    /// БЕЗОПАСНОСТЬ: эти заголовки клиент может подделать, если ходит к сервису напрямую,
    /// минуя nginx. В docker compose это невозможно (порт API не экспонирован), но в проде
    /// убедитесь, что reverse proxy перетирает входящие X-Real-IP/X-Forwarded-For заголовки
    /// своими значениями. nginx через `grpc_set_header` это делает по умолчанию.
    /// </summary>
    private static string ResolveClientIp(ServerCallContext context)
    {
        // 1. X-Real-IP
        var realIp = context.RequestHeaders.GetValue("x-real-ip");
        if (!string.IsNullOrWhiteSpace(realIp))
            return realIp.Trim();

        // 2. X-Forwarded-For — может быть "client, proxy1, proxy2", берём первый
        var forwardedFor = context.RequestHeaders.GetValue("x-forwarded-for");
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            var firstHop = forwardedFor.Split(',', 2)[0].Trim();
            if (!string.IsNullOrEmpty(firstHop))
                return firstHop;
        }

        // 3. context.Peer (формат "ipv4:172.18.0.5:54321") — отрезаем эфемерный порт.
        // Каждое новое TCP-соединение от nginx даёт другой порт; без обрезки счётчик
        // никогда бы не превысил 1.
        var peer = context.Peer;
        if (string.IsNullOrEmpty(peer))
            return "unknown";

        var lastColon = peer.LastIndexOf(':');
        return lastColon > 0 ? peer[..lastColon] : peer;
    }

    private record RateLimitRule(int MaxRequests, TimeSpan Window);
}