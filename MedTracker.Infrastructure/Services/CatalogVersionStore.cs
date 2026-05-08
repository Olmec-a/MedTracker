using MedTracker.Application.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace MedTracker.Infrastructure.Services;

public class CatalogVersionStore : ICatalogVersionStore
{
    private const string VersionKey = "medtracker:catalog:version";
    private readonly IConnectionMultiplexer _redis;

    public CatalogVersionStore(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<long> GetCurrentAsync(CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(VersionKey);
        if (value.IsNullOrEmpty)
        {
            // Lazy init — без race condition: SET NX
            await db.StringSetAsync(VersionKey, 1, when: When.NotExists);
            value = await db.StringGetAsync(VersionKey);
        }
        return (long)value;
    }

    public async Task<long> BumpAsync(CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        return await db.StringIncrementAsync(VersionKey);
    }
}

public class CatalogCacheInvalidator : ICatalogCacheInvalidator
{
    private readonly ICatalogVersionStore _versionStore;
    private readonly ILogger<CatalogCacheInvalidator> _logger;

    public CatalogCacheInvalidator(
        ICatalogVersionStore versionStore,
        ILogger<CatalogCacheInvalidator> logger)
    {
        _versionStore = versionStore;
        _logger = logger;
    }

    public async Task InvalidateAsync(CancellationToken ct = default)
    {
        var newVersion = await _versionStore.BumpAsync(ct);
        // L1 во всех репликах истечёт через VersionLocalTtl (15 сек) — после этого
        // все запросы пойдут уже с новой версией. Старые ключи в Redis истекут по своему TTL.
        _logger.LogInformation("Catalog cache invalidated. New version: {Version}", newVersion);
    }
}