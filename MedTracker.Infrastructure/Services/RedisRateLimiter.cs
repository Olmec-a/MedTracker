using MedTracker.Application.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace MedTracker.Infrastructure.Services;

/// <summary>
/// Distributed rate limiter поверх Redis. Fixed-window семантика —
/// просто, атомарно (один Lua-скрипт), и работает корректно для multi-replica.
///
/// Sliding window можно сделать через sorted sets, но это сложнее и дороже —
/// для anti-abuse в /Login и /Register fixed window достаточен.
/// </summary>
public class RedisRateLimiter : IRateLimiter
{
    // Lua-скрипт: атомарный INCR. При первом инкременте ставит EXPIRE.
    // Возвращает (current, ttl).
    private const string IncrScript = """
        local current = redis.call('INCR', KEYS[1])
        if tonumber(current) == 1 then
            redis.call('EXPIRE', KEYS[1], ARGV[1])
        end
        local ttl = redis.call('TTL', KEYS[1])
        return {current, ttl}
        """;

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisRateLimiter> _logger;

    public RedisRateLimiter(IConnectionMultiplexer redis, ILogger<RedisRateLimiter> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<RateLimitResult> CheckAsync(
        string key, int limit, TimeSpan window, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var redisKey = $"medtracker:ratelimit:{key}";
            var result = (RedisResult[]?)await db.ScriptEvaluateAsync(
                IncrScript,
                keys: new RedisKey[] { redisKey },
                values: new RedisValue[] { (int)window.TotalSeconds });

            if (result is null || result.Length < 2)
                return new RateLimitResult(true, 0, limit, window);

            var current = (int)result[0];
            var ttl = (int)result[1];
            // ttl == -1 если ключ есть без expire (не должно случиться в нашем сценарии),
            // ttl == -2 если ключ отсутствует (тоже не наш кейс после INCR).
            var resetAfter = ttl > 0 ? TimeSpan.FromSeconds(ttl) : window;

            return new RateLimitResult(current <= limit, current, limit, resetAfter);
        }
        catch (RedisException ex)
        {
            // Fail-open: если Redis отвалился — НЕ блокировать пользователей.
            // В логе шумим, чтобы поймать проблемы.
            _logger.LogError(ex, "Redis rate limit check failed for key {Key}; allowing request", key);
            return new RateLimitResult(true, 0, limit, window);
        }
    }
}