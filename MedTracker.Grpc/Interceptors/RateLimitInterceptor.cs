using Grpc.Core;
using Grpc.Core.Interceptors;
using MedTracker.Application.Interfaces;

namespace MedTracker.Grpc.Interceptors;

/// <summary>
/// Distributed rate limiter (Redis-backed). Корректно работает на нескольких репликах:
/// атакующий не может обойти лимит, попадая на разные инстансы.
/// </summary>
public class RateLimitInterceptor : Interceptor
{
    private static readonly Dictionary<string, RateLimitRule> Rules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/medtracker.AuthService/Login"]                = new(5, TimeSpan.FromMinutes(1)),
        ["/medtracker.AuthService/Register"]             = new(3, TimeSpan.FromHours(1)),
        ["/medtracker.AuthService/RefreshToken"]         = new(10, TimeSpan.FromMinutes(1)),

        ["/medtracker.AuthService/ResendConfirmation"]   = new(3, TimeSpan.FromHours(1)),
        ["/medtracker.AuthService/ConfirmEmail"]         = new(10, TimeSpan.FromMinutes(5)),

        ["/medtracker.AuthService/RequestPasswordReset"] = new(3, TimeSpan.FromHours(1)),
        ["/medtracker.AuthService/ResetPassword"]        = new(5, TimeSpan.FromMinutes(15))
    };

    private readonly IRateLimiter _rateLimiter;
    private readonly ILogger<RateLimitInterceptor> _logger;

    public RateLimitInterceptor(IRateLimiter rateLimiter, ILogger<RateLimitInterceptor> logger)
    {
        _rateLimiter = rateLimiter;
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
        if (!Rules.TryGetValue(context.Method, out var rule))
            return;

        // Peer обычно "ipv4:1.2.3.4:5678". Берём только адрес — без порта,
        // иначе атакующий обходит лимит, переключая порт.
        var peer = NormalizePeer(context.Peer);
        var key = $"{peer}:{context.Method}";

        var result = await _rateLimiter.CheckAsync(key, rule.MaxRequests, rule.Window, context.CancellationToken);

        if (!result.Allowed)
        {
            _logger.LogWarning(
                "Rate limit exceeded for {Peer} on {Method}: {Current}/{Limit}, resets in {Reset}s",
                peer, context.Method, result.CurrentCount, result.Limit, (int)result.ResetAfter.TotalSeconds);

            // Стандартный gRPC retry-info: подсказка клиенту, через сколько повторять.
            var trailers = new Metadata { { "retry-after-seconds", ((int)result.ResetAfter.TotalSeconds).ToString() } };

            throw new RpcException(
                new Status(StatusCode.ResourceExhausted,
                    $"Rate limit exceeded. Max {rule.MaxRequests} requests per {rule.Window.TotalSeconds:0}s. " +
                    $"Try again in ~{(int)result.ResetAfter.TotalSeconds}s."),
                trailers);
        }
    }

    private static string NormalizePeer(string? peer)
    {
        if (string.IsNullOrEmpty(peer)) return "unknown";
        // "ipv4:1.2.3.4:5678" → "1.2.3.4"
        // "ipv6:[::1]:5678"   → "::1"
        var cleaned = peer.StartsWith("ipv4:") ? peer[5..]
                    : peer.StartsWith("ipv6:") ? peer[5..]
                    : peer;
        var lastColon = cleaned.LastIndexOf(':');
        return lastColon > 0 ? cleaned[..lastColon].Trim('[', ']') : cleaned;
    }

    private record RateLimitRule(int MaxRequests, TimeSpan Window);
}