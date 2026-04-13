using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MedTracker.Application.DTOs;
using MedTracker.Application.Interfaces;
using MedTracker.Grpc.Interceptors;


namespace MedTracker.Grpc.Services;

public class ExternalMedicationGrpcService : ExternalMedicationService.ExternalMedicationServiceBase
{
    private readonly IExternalMedicationService _service;

    public ExternalMedicationGrpcService(IExternalMedicationService service)
    {
        _service = service;
    }

    public override async Task<ExternalMedResponse> AddExternalMedication(AddExternalMedRequest request, ServerCallContext context)
    {
        var userId = context.GetUserId();
        var dto = new CreateExternalMedicationDto(
            request.Name,
            request.Dosage,
            request.Date.ToDateTime(),
            request.Comment?.Value);
        var result = await _service.AddAsync(userId, dto, context.CancellationToken);
        return ToResponse(result);
    }

    public override async Task<ExternalMedsListResponse> GetMyExternalMedications(ExternalMedsQuery request, ServerCallContext context)
    {
        var userId = context.GetUserId();
        DateTime? from = request.DateRange?.From?.ToDateTime();
        DateTime? to = request.DateRange?.To?.ToDateTime();
        int page = request.Pagination?.Page ?? 1;
        int pageSize = request.Pagination?.PageSize ?? 20;

        var result = await _service.GetAsync(userId, from, to, page, pageSize, context.CancellationToken);
        var response = new ExternalMedsListResponse { TotalCount = result.TotalCount };
        response.Medications.AddRange(result.Items.Select(ToResponse));
        return response;
    }

    public override async Task<Empty> DeleteExternalMedication(DeleteExternalMedRequest request, ServerCallContext context)
    {
        var userId = context.GetUserId();
        await _service.DeleteAsync(userId, Guid.Parse(request.Id), context.CancellationToken);
        return new Empty();
    }

    private static ExternalMedResponse ToResponse(ExternalMedicationDto dto) => new()
    {
        Id = dto.Id.ToString(),
        Name = dto.Name,
        Dosage = dto.Dosage,
        Date = Timestamp.FromDateTime(DateTime.SpecifyKind(dto.Date, DateTimeKind.Utc)),
        Comment = dto.Comment ?? string.Empty
    };
}