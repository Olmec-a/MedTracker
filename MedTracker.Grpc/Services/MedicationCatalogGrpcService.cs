using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MedTracker.Application.Interfaces;
using MedTracker.Grpc.Protos;

namespace MedTracker.Grpc.Services;

public class MedicationCatalogGrpcService : MedicationCatalogService.MedicationCatalogServiceBase
{
    private readonly IMedicationCatalogService _service;

    public MedicationCatalogGrpcService(IMedicationCatalogService service)
    {
        _service = service;
    }

    public override async Task<DiagnosesListResponse> GetDiagnoses(Empty request, ServerCallContext context)
    {
        var diagnoses = await _service.GetDiagnosesAsync(context.CancellationToken);
        var response = new DiagnosesListResponse();
        response.Diagnoses.AddRange(diagnoses.Select(d => new DiagnosisDto
        {
            Id = d.Id.ToString(),
            Name = d.Name
        }));
        return response;
    }

    public override async Task<MedicationsListResponse> GetMedicationsByDiagnosis(DiagnosisIdRequest request, ServerCallContext context)
    {
        var medications = await _service.GetMedicationsByDiagnosisAsync(Guid.Parse(request.DiagnosisId), context.CancellationToken);
        var response = new MedicationsListResponse();
        response.Medications.AddRange(medications.Select(m => new MedicationDto
        {
            Id = m.Id.ToString(),
            DiagnosisId = m.DiagnosisId.ToString(),
            HormonalGroup = m.HormonalGroup,
            Inn = m.INN,
            TradeName = m.TradeName,
            Dosage = m.Dosage,
            Form = m.Form,
            Frequency = m.Frequency,
            Diet = m.Diet
        }));
        return response;
    }

    public override async Task<SupplementsListResponse> GetSupplementsByMedication(MedicationIdRequest request, ServerCallContext context)
    {
        var supplements = await _service.GetSupplementsByMedicationAsync(Guid.Parse(request.MedicationId), context.CancellationToken);
        var response = new SupplementsListResponse();
        response.Supplements.AddRange(supplements.Select(s => new SupplementDto
        {
            Id = s.Id.ToString(),
            MedicationId = s.MedicationId.ToString(),
            Name = s.Name,
            Dosage = s.Dosage,
            Frequency = s.Frequency
        }));
        return response;
    }

    public override async Task<SideEffectsListResponse> GetSideEffectsByMedication(MedicationIdRequest request, ServerCallContext context)
    {
        var sideEffects = await _service.GetSideEffectsByMedicationAsync(Guid.Parse(request.MedicationId), context.CancellationToken);
        var response = new SideEffectsListResponse();
        response.SideEffects.AddRange(sideEffects.Select(se => new SideEffectDto
        {
            Id = se.Id.ToString(),
            MedicationId = se.MedicationId.ToString(),
            Name = se.Name
        }));
        return response;
    }
}