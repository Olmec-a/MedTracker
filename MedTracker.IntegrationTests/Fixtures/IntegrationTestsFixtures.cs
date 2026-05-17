using MedTracker.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace MedTracker.IntegrationTests.Fixtures;

/// <summary>
/// Поднимает реальные Postgres + Redis в Docker один раз на весь test-run.
/// Между тестами таблицы truncate'аются, Redis flush'ится — изоляция через очистку,
/// не через пересоздание контейнеров (это было бы +30 сек на каждый тест).
/// </summary>
public class IntegrationTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("medtracker_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    public string PostgresConnectionString => _postgres.GetConnectionString();
    public string RedisConnectionString => _redis.GetConnectionString();

    public IConnectionMultiplexer Redis { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        // AllowAdmin=true — нужно для FLUSHDB между тестами.
        // В тестовой среде безопасно, в проде НЕ ставить.
        var redisOptions = ConfigurationOptions.Parse(RedisConnectionString);
        redisOptions.AllowAdmin = true;
        Redis = await ConnectionMultiplexer.ConnectAsync(redisOptions);

        await using var ctx = CreateDbContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        Redis?.Dispose();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    /// <summary>Создаёт свежий DbContext с подключением к контейнеру.</summary>
    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(PostgresConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>
    /// Очищает все прикладные таблицы. Hangfire-таблицы не трогаем — тесты не используют.
    /// TRUNCATE ... CASCADE — быстрее, чем DELETE + reset sequences.
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        await using var ctx = CreateDbContext();
        await ctx.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE 
                "OutboxMessages",
                "RefreshTokens",
                "UserSideEffectLogs",
                "MenstrualCycleEntries",
                "ExternalMedications",
                "UserSupplements",
                "UserMedications",
                "UserDiagnoses",
                "ImportRecords",
                "Users",
                "SideEffects",
                "Supplements",
                "Medications",
                "Diagnoses"
            RESTART IDENTITY CASCADE;
            """);
    }

    /// <summary>Полная очистка Redis между тестами.</summary>
    public async Task ResetRedisAsync()
    {
        var endpoints = Redis.GetEndPoints();
        foreach (var endpoint in endpoints)
        {
            var server = Redis.GetServer(endpoint);
            await server.FlushDatabaseAsync();
        }
    }
}

/// <summary>
/// xUnit collection — гарантирует, что fixture создаётся один раз на ВСЕ тесты,
/// а не на каждый класс. Иначе Postgres/Redis поднимались бы N раз.
/// </summary>
[CollectionDefinition("Integration")]
public class IntegrationCollection : ICollectionFixture<IntegrationTestFixture> { }