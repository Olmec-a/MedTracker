using MedTracker.Application.Interfaces;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace MedTracker.Infrastructure.Services;

/// <summary>
/// Distributed cache статуса пользователя для AuthInterceptor'а.
/// L1 (in-memory) на 30 сек — большинство hits сюда, без round-trip к Redis.
/// L2 (Redis) на 2 мин — другие реплики увидят изменения lockout/удаления через ~30 сек.
///
/// Используем IServiceScopeFactory, потому что IUserRepository — scoped,
/// а UserStatusCache регистрируется как singleton (живёт всё время).
/// </summary>
public class UserStatusCache : IUserStatusCache
{
    private static readonly HybridCacheEntryOptions Options = new()
    {
        Expiration = TimeSpan.FromMinutes(2),
        LocalCacheExpiration = TimeSpan.FromSeconds(30)
    };

    private readonly HybridCache _cache;
    private readonly IServiceScopeFactory _scopeFactory;

    public UserStatusCache(HybridCache cache, IServiceScopeFactory scopeFactory)
    {
        _cache = cache;
        _scopeFactory = scopeFactory;
    }

    public async Task<UserStatusSnapshot?> GetAsync(Guid userId, CancellationToken ct = default)
    {
        return await _cache.GetOrCreateAsync(
            BuildKey(userId),
            async innerCt =>
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                var user = await repo.GetByIdAsync(userId, innerCt);
                return user == null
                    ? new UserStatusSnapshot(false, null)
                    : new UserStatusSnapshot(true, user.LockoutUntil);
            },
            Options,
            cancellationToken: ct);
    }

    public async Task InvalidateAsync(Guid userId, CancellationToken ct = default)
        => await _cache.RemoveAsync(BuildKey(userId), ct);

    private static string BuildKey(Guid userId) => $"user-status:{userId}";
}