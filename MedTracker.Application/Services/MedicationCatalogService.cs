using MedTracker.Application.DTOs;
using MedTracker.Application.Interfaces;
using Microsoft.Extensions.Caching.Hybrid;

namespace MedTracker.Application.Services;

/// <summary>
/// Каталог справочника. Кеширование через HybridCache (L1 in-memory + L2 Redis).
/// Стратегия инвалидации — версионирование ключей: текущая версия лежит в Redis
/// и инкрементируется при импорте Excel (см. ICatalogCacheInvalidator).
/// Ключ каталога = "catalog:v{N}:..." — после bump'а старые ключи никем не читаются
/// и истекают по TTL.
/// </summary>
public class MedicationCatalogService : IMedicationCatalogService
{
    private const string VersionCacheKey = "catalog:current-version";
    private static readonly TimeSpan VersionLocalTtl = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan VersionDistributedTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CatalogTtl = TimeSpan.FromHours(1);

    private readonly IDiagnosisRepository _diagnosisRepo;
    private readonly IMedicationRepository _medicationRepo;
    private readonly ISupplementRepository _supplementRepo;
    private readonly ISideEffectRepository _sideEffectRepo;
    private readonly HybridCache _cache;
    private readonly ICatalogVersionStore _versionStore;

    public MedicationCatalogService(
        IDiagnosisRepository diagnosisRepo,
        IMedicationRepository medicationRepo,
        ISupplementRepository supplementRepo,
        ISideEffectRepository sideEffectRepo,
        HybridCache cache,
        ICatalogVersionStore versionStore)
    {
        _diagnosisRepo = diagnosisRepo;
        _medicationRepo = medicationRepo;
        _supplementRepo = supplementRepo;
        _sideEffectRepo = sideEffectRepo;
        _cache = cache;
        _versionStore = versionStore;
    }

    public async Task<List<DiagnosisDto>> GetDiagnosesAsync(CancellationToken ct = default)
    {
        var version = await GetVersionAsync(ct);
        return await _cache.GetOrCreateAsync(
            $"catalog:v{version}:diagnoses",
            async innerCt =>
            {
                var diagnoses = await _diagnosisRepo.GetAllAsync(innerCt);
                return diagnoses.Select(d => new DiagnosisDto(d.Id, d.Name)).ToList();
            },
            new HybridCacheEntryOptions { Expiration = CatalogTtl, LocalCacheExpiration = TimeSpan.FromMinutes(5) },
            cancellationToken: ct);
    }

    public async Task<PaginatedResultDto<MedicationDto>> GetMedicationsByDiagnosisAsync(
        Guid diagnosisId, int page, int pageSize, CancellationToken ct = default)
    {
        var version = await GetVersionAsync(ct);
        return await _cache.GetOrCreateAsync(
            $"catalog:v{version}:meds:{diagnosisId}:p{page}:s{pageSize}",
            async innerCt =>
            {
                var (items, total) = await _medicationRepo.GetByDiagnosisIdAsync(diagnosisId, page, pageSize, innerCt);
                var dtos = items.Select(m => new MedicationDto(
                    m.Id, m.DiagnosisId, m.HormonalGroup, m.INN,
                    m.TradeName, m.Dosage, m.Form, m.Frequency, m.Diet)).ToList();
                return new PaginatedResultDto<MedicationDto>(dtos, total);
            },
            new HybridCacheEntryOptions { Expiration = CatalogTtl, LocalCacheExpiration = TimeSpan.FromMinutes(5) },
            cancellationToken: ct);
    }

    public async Task<PaginatedResultDto<SupplementDto>> GetSupplementsByMedicationAsync(
        Guid medicationId, int page, int pageSize, CancellationToken ct = default)
    {
        var version = await GetVersionAsync(ct);
        return await _cache.GetOrCreateAsync(
            $"catalog:v{version}:supps:{medicationId}:p{page}:s{pageSize}",
            async innerCt =>
            {
                var (items, total) = await _supplementRepo.GetByMedicationIdAsync(medicationId, page, pageSize, innerCt);
                var dtos = items.Select(s => new SupplementDto(s.Id, s.MedicationId, s.Name, s.Dosage, s.Frequency)).ToList();
                return new PaginatedResultDto<SupplementDto>(dtos, total);
            },
            new HybridCacheEntryOptions { Expiration = CatalogTtl, LocalCacheExpiration = TimeSpan.FromMinutes(5) },
            cancellationToken: ct);
    }

    public async Task<PaginatedResultDto<SideEffectDto>> GetSideEffectsByMedicationAsync(
        Guid medicationId, int page, int pageSize, CancellationToken ct = default)
    {
        var version = await GetVersionAsync(ct);
        return await _cache.GetOrCreateAsync(
            $"catalog:v{version}:ses:{medicationId}:p{page}:s{pageSize}",
            async innerCt =>
            {
                var (items, total) = await _sideEffectRepo.GetByMedicationIdAsync(medicationId, page, pageSize, innerCt);
                var dtos = items.Select(se => new SideEffectDto(se.Id, se.MedicationId, se.Name)).ToList();
                return new PaginatedResultDto<SideEffectDto>(dtos, total);
            },
            new HybridCacheEntryOptions { Expiration = CatalogTtl, LocalCacheExpiration = TimeSpan.FromMinutes(5) },
            cancellationToken: ct);
    }

    /// <summary>
    /// Текущая версия каталога. Кешируется и локально (15 сек), и в Redis (5 мин).
    /// После import-а ICatalogCacheInvalidator делает Bump → счётчик в Redis инкрементится.
    /// Через 15 секунд (макс) все реплики увидят новую версию и начнут читать свежие данные.
    /// </summary>
    private async Task<long> GetVersionAsync(CancellationToken ct)
    {
        return await _cache.GetOrCreateAsync(
            VersionCacheKey,
            async innerCt => await _versionStore.GetCurrentAsync(innerCt),
            new HybridCacheEntryOptions
            {
                Expiration = VersionDistributedTtl,
                LocalCacheExpiration = VersionLocalTtl
            },
            cancellationToken: ct);
    }
}