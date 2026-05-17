using FluentAssertions;
using MedTracker.Application.Interfaces;
using MedTracker.Application.Services;
using MedTracker.Domain.Entities;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MedTracker.Tests.Catalog;

public class MedicationCatalogServiceTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly HybridCache _cache;
    private readonly IDiagnosisRepository _diagnosisRepo = Substitute.For<IDiagnosisRepository>();
    private readonly IMedicationRepository _medicationRepo = Substitute.For<IMedicationRepository>();
    private readonly ISupplementRepository _supplementRepo = Substitute.For<ISupplementRepository>();
    private readonly ISideEffectRepository _sideEffectRepo = Substitute.For<ISideEffectRepository>();
    private readonly ICatalogVersionStore _versionStore = Substitute.For<ICatalogVersionStore>();

    public MedicationCatalogServiceTests()
    {
        // Реальный HybridCache с in-memory L2 — поведение версионирования
        // и stampede protection такое же, как в проде с Redis L2.
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.AddHybridCache();
        _sp = services.BuildServiceProvider();
        _cache = _sp.GetRequiredService<HybridCache>();
    }

    private MedicationCatalogService CreateSut() => new(
        _diagnosisRepo, _medicationRepo, _supplementRepo, _sideEffectRepo,
        _cache, _versionStore);

    [Fact]
    public async Task GetDiagnoses_FirstCall_HitsRepository()
    {
        _versionStore.GetCurrentAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _diagnosisRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Diagnosis>
            {
                new() { Id = Guid.NewGuid(), Name = "Diabetes" }
            });

        var result = await CreateSut().GetDiagnosesAsync();

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Diabetes");
        await _diagnosisRepo.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetDiagnoses_SecondCall_UsesCacheAndDoesNotHitRepo()
    {
        _versionStore.GetCurrentAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _diagnosisRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Diagnosis> { new() { Id = Guid.NewGuid(), Name = "X" } });

        var sut = CreateSut();
        await sut.GetDiagnosesAsync();
        await sut.GetDiagnosesAsync();
        await sut.GetDiagnosesAsync();

        await _diagnosisRepo.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetDiagnoses_AfterVersionBump_ReReadsRepository()
    {
        // Сценарий: импорт Excel → версия инкрементилась → следующий запрос
        // строит ключ с новой версией → старый кеш не используется.
        var diagSeq = new Queue<List<Diagnosis>>();
        diagSeq.Enqueue(new List<Diagnosis> { new() { Id = Guid.NewGuid(), Name = "OldData" } });
        diagSeq.Enqueue(new List<Diagnosis> { new() { Id = Guid.NewGuid(), Name = "NewData" } });

        _diagnosisRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ => diagSeq.Dequeue());

        var sut = CreateSut();

        // v1: первый вызов → "OldData"
        _versionStore.GetCurrentAsync(Arg.Any<CancellationToken>()).Returns(1L);
        var first = await sut.GetDiagnosesAsync();
        first[0].Name.Should().Be("OldData");

        // v2: версия поменялась → новый ключ → новый запрос к репо → "NewData"
        // Очищаем локальный кеш версии, иначе HybridCache отдаст старое значение
        await _cache.RemoveAsync("catalog:current-version");
        _versionStore.GetCurrentAsync(Arg.Any<CancellationToken>()).Returns(2L);

        var second = await sut.GetDiagnosesAsync();
        second[0].Name.Should().Be("NewData");

        await _diagnosisRepo.Received(2).GetAllAsync(Arg.Any<CancellationToken>());
    }

    public void Dispose() => _sp.Dispose();
}