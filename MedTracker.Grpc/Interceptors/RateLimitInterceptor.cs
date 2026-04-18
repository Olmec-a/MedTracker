using System.Collections.Concurrent;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace MedTracker.Grpc.Interceptors;

/// <summary>
/// In-memory sliding window rate limiter per IP per method.
/// Для production заменить на распределённое хранилище (Redis).
/// </summary>
public class RateLimitInterceptor : Interceptor
{
    private static readonly ConcurrentDictionary<string, List<DateTime>> _requests = new();

    private static readonly Dictionary<string, RateLimitRule> _rules = new(StringComparer.OrdinalIgnoreCase)
    {
        // method path → (max requests, window)
        ["/medtracker.AuthService/Login"] = new(5, TimeSpan.FromMinutes(1)),
        ["/medtracker.AuthService/Register"] = new(3, TimeSpan.FromHours(1)),
        ["/medtracker.AuthService/RefreshToken"] = new(10, TimeSpan.FromMinutes(1))
    };

    private readonly ILogger<RateLimitInterceptor> _logger;

    public RateLimitInterceptor(ILogger<RateLimitInterceptor> logger)
    {
        _logger = logger;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        CheckRateLimit(context);
        return await continuation(request, context);
    }

    private void CheckRateLimit(ServerCallContext context)
    {
        if (!_rules.TryGetValue(context.Method, out var rule))
            return;

        var clientIp = context.Peer ?? "unknown";
        var key = $"{clientIp}:{context.Method}";
        var now = DateTime.UtcNow;
        var windowStart = now - rule.Window;

        var timestamps = _requests.GetOrAdd(key, _ => new List<DateTime>());

        lock (timestamps)
        {
            timestamps.RemoveAll(t => t < windowStart);

            if (timestamps.Count >= rule.MaxRequests)
            {
                _logger.LogWarning("Rate limit exceeded for {Peer} on {Method}", clientIp, context.Method);
                throw new RpcException(new Status(
                    StatusCode.ResourceExhausted,
                    $"Rate limit exceeded. Max {rule.MaxRequests} requests per {rule.Window.TotalSeconds:0}s."));
            }

            timestamps.Add(now);
        }
    }

    private record RateLimitRule(int MaxRequests, TimeSpan Window);
}