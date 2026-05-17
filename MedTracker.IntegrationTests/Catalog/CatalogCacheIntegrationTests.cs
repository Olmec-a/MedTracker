using FluentAssertions;
using MedTracker.Application.Services;
using MedTracker.Domain.Entities;
using MedTracker.Infrastructure.Repositories;
using MedTracker.Infrastructure.Services;
using MedTracker.IntegrationTests.Fixtures;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedTracker.IntegrationTests.Catalog;

[Collection("Integration")]
public class CatalogCacheIntegrationTests : IAsyncLifetime
{
    private readonly IntegrationTestFixture _fixture;
    private readonly ServiceProvider _sp;
    private readonly HybridCache _cache;

    public CatalogCacheIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;

        // Поднимаем HybridCache с настоящим Redis L2 (того же контейнера, что для всего теста)
        var services = new ServiceCollection();
        services.AddStackExchangeRedisCache(opts =>
        {
            opts.Configuration = _fixture.RedisConnectionString;
            opts.InstanceName = "test:";
        });
        services.AddHybridCache();
        _sp = services.BuildServiceProvider();
        _cache = _sp.GetRequiredService<HybridCache>();
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();
        await _fixture.ResetRedisAsync();
    }

    public Task DisposeAsync()
    {
        _sp.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task FullCycle_Read_Cache_Invalidate_ReReads()
    {
        // SETUP: один диагноз в БД
        await using (var ctx = _fixture.CreateDbContext())
        {
            await ctx.Diagnoses.AddAsync(new Diagnosis
            {
                Id = Guid.NewGuid(),
                Name = "Initial Data"
            });
            await ctx.SaveChangesAsync();
        }

        var sut = CreateSut();

        // 1-й запрос — кеш пустой, идёт в БД
        var first = await sut.GetDiagnosesAsync();
        first.Should().HaveCount(1);
        first[0].Name.Should().Be("Initial Data");

        // 2-й запрос — должен взять из кеша.
        // Проверяем косвенно: даже если БД поменять, ответ останется тем же (потому что кеш).
        await using (var ctx = _fixture.CreateDbContext())
        {
            await ctx.Diagnoses.AddAsync(new Diagnosis
            {
                Id = Guid.NewGuid(),
                Name = "Should Not Appear Until Invalidation"
            });
            await ctx.SaveChangesAsync();
        }

        var second = await sut.GetDiagnosesAsync();
        second.Should().HaveCount(1, "кеш ещё не инвалидирован, новый диагноз не виден");
        second[0].Name.Should().Be("Initial Data");

        // 3-й шаг: инвалидируем кеш через bump версии
        var versionStore = new CatalogVersionStore(_fixture.Redis);
        var invalidator = new CatalogCacheInvalidator(versionStore, NullLogger<CatalogCacheInvalidator>.Instance);
        await invalidator.InvalidateAsync();

        // Локальный кеш версии в HybridCache живёт 15 секунд (см. MedicationCatalogService).
        // Чтобы тест был быстрым — принудительно очищаем
        await _cache.RemoveAsync("catalog:current-version");

        // 4-й запрос — теперь видны оба диагноза, потому что версия выросла → новый ключ → новое чтение
        var third = await sut.GetDiagnosesAsync();
        third.Should().HaveCount(2);
        third.Should().Contain(d => d.Name == "Should Not Appear Until Invalidation");
    }

    [Fact]
    public async Task VersionStore_BumpAsync_AtomicallyIncrements()
    {
        var store = new CatalogVersionStore(_fixture.Redis);

        var initial = await store.GetCurrentAsync();
        initial.Should().Be(1, "при первом обращении инициализируется в 1");

        // 50 параллельных bump'ов
        var tasks = Enumerable.Range(0, 50).Select(_ => store.BumpAsync());
        var results = await Task.WhenAll(tasks);

        // Все вернутые значения должны быть уникальны (от 2 до 51 включительно)
        results.ToHashSet().Should().HaveCount(50, "INCR в Redis атомарен → нет дублей");
        results.Min().Should().Be(2);
        results.Max().Should().Be(51);

        // Финальное значение в Redis
        var final = await store.GetCurrentAsync();
        final.Should().Be(51);
    }

    private MedicationCatalogService CreateSut()
    {
        // Все зависимости настоящие
        var dbContext = _fixture.CreateDbContext();
        var diagnosisRepo = new DiagnosisRepository(dbContext);
        var medicationRepo = new MedicationRepository(dbContext);
        var supplementRepo = new SupplementRepository(dbContext);
        var sideEffectRepo = new SideEffectRepository(dbContext);
        var versionStore = new CatalogVersionStore(_fixture.Redis);

        return new MedicationCatalogService(
            diagnosisRepo, medicationRepo, supplementRepo, sideEffectRepo,
            _cache, versionStore);
    }
}