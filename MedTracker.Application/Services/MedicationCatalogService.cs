using MedTracker.Application.DTOs;
using MedTracker.Application.Interfaces;

namespace MedTracker.Application.Services;

public class MedicationCatalogService : IMedicationCatalogService
{
    private readonly IDiagnosisRepository _diagnosisRepo;
    private readonly IMedicationRepository _medicationRepo;
    private readonly ISupplementRepository _supplementRepo;
    private readonly ISideEffectRepository _sideEffectRepo;

    public MedicationCatalogService(
        IDiagnosisRepository diagnosisRepo,
        IMedicationRepository medicationRepo,
        ISupplementRepository supplementRepo,
        ISideEffectRepository sideEffectRepo)
    {
        _diagnosisRepo = diagnosisRepo;
        _medicationRepo = medicationRepo;
        _supplementRepo = supplementRepo;
        _sideEffectRepo = sideEffectRepo;
    }

    public async Task<List<DiagnosisDto>> GetDiagnosesAsync(CancellationToken ct = default)
    {
        var diagnoses = await _diagnosisRepo.GetAllAsync(ct);
        return diagnoses.Select(d => new DiagnosisDto(d.Id, d.Name)).ToList();
    }

    public async Task<PaginatedResultDto<MedicationDto>> GetMedicationsByDiagnosisAsync(Guid diagnosisId, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _medicationRepo.GetByDiagnosisIdAsync(diagnosisId, page, pageSize, ct);
        var dtos = items.Select(m => new MedicationDto(
            m.Id, m.DiagnosisId, m.HormonalGroup, m.INN,
            m.TradeName, m.Dosage, m.Form, m.Frequency, m.Diet)).ToList();
        return new PaginatedResultDto<MedicationDto>(dtos, total);
    }

    public async Task<PaginatedResultDto<SupplementDto>> GetSupplementsByMedicationAsync(Guid medicationId, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _supplementRepo.GetByMedicationIdAsync(medicationId, page, pageSize, ct);
        var dtos = items.Select(s => new SupplementDto(s.Id, s.MedicationId, s.Name, s.Dosage, s.Frequency)).ToList();
        return new PaginatedResultDto<SupplementDto>(dtos, total);
    }

    public async Task<PaginatedResultDto<SideEffectDto>> GetSideEffectsByMedicationAsync(Guid medicationId, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _sideEffectRepo.GetByMedicationIdAsync(medicationId, page, pageSize, ct);
        var dtos = items.Select(se => new SideEffectDto(se.Id, se.MedicationId, se.Name)).ToList();
        return new PaginatedResultDto<SideEffectDto>(dtos, total);
    }
}