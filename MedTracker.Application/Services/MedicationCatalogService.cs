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

    public async Task<List<MedicationDto>> GetMedicationsByDiagnosisAsync(Guid diagnosisId, CancellationToken ct = default)
    {
        var medications = await _medicationRepo.GetByDiagnosisIdAsync(diagnosisId, ct);
        return medications.Select(m => new MedicationDto(
            m.Id, m.DiagnosisId, m.HormonalGroup, m.INN,
            m.TradeName, m.Dosage, m.Form, m.Frequency, m.Diet)).ToList();
    }

    public async Task<List<SupplementDto>> GetSupplementsByMedicationAsync(Guid medicationId, CancellationToken ct = default)
    {
        var supplements = await _supplementRepo.GetByMedicationIdAsync(medicationId, ct);
        return supplements.Select(s => new SupplementDto(s.Id, s.MedicationId, s.Name, s.Dosage, s.Frequency)).ToList();
    }

    public async Task<List<SideEffectDto>> GetSideEffectsByMedicationAsync(Guid medicationId, CancellationToken ct = default)
    {
        var sideEffects = await _sideEffectRepo.GetByMedicationIdAsync(medicationId, ct);
        return sideEffects.Select(se => new SideEffectDto(se.Id, se.MedicationId, se.Name)).ToList();
    }
}