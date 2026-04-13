using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MedTracker.Application.DTOs;
using MedTracker.Application.Interfaces;
using MedTracker.Grpc.Interceptors;
using MedTracker.Grpc.Protos;

namespace MedTracker.Grpc.Services;

public class UserMedicationGrpcService : UserMedicationService.UserMedicationServiceBase
{
    private readonly IUserMedicationService _service;

    public UserMedicationGrpcService(IUserMedicationService service)
    {
        _service = service;
    }

    public override async Task<UserMedicationResponse> AssignMedication(AssignMedicationRequest request, ServerCallContext context)
    {
        var userId = context.GetUserId();
        var dto = new AssignMedicationDto(
            Guid.Parse(request.MedicationId),
            request.StartDate.ToDateTime(),
            request.EndDate?.ToDateTime());
        var result = await _service.AssignMedicationAsync(userId, dto, context.CancellationToken);
        return ToMedResponse(result);
    }

    public override async Task<Empty> RemoveMedication(RemoveMedicationRequest request, ServerCallContext context)
    {
        var userId = context.GetUserId();
        await _service.RemoveMedicationAsync(userId, Guid.Parse(request.UserMedicationId), context.CancellationToken);
        return new Empty();
    }

    public override async Task<UserMedicationsListResponse> GetMyMedications(Empty request, ServerCallContext context)
    {
        var userId = context.GetUserId();
        var medications = await _service.GetUserMedicationsAsync(userId, context.CancellationToken);
        var response = new UserMedicationsListResponse();
        response.Medications.AddRange(medications.Select(ToMedResponse));
        return response;
    }

    public override async Task<UserSupplementResponse> AssignSupplement(AssignSupplementRequest request, ServerCallContext context)
    {
        var userId = context.GetUserId();
        var dto = new AssignSupplementDto(
            Guid.Parse(request.SupplementId),
            request.StartDate.ToDateTime(),
            request.EndDate?.ToDateTime());
        var result = await _service.AssignSupplementAsync(userId, dto, context.CancellationToken);
        return ToSuppResponse(result);
    }

    public override async Task<Empty> RemoveSupplement(RemoveSupplementRequest request, ServerCallContext context)
    {
        var userId = context.GetUserId();
        await _service.RemoveSupplementAsync(userId, Guid.Parse(request.UserSupplementId), context.CancellationToken);
        return new Empty();
    }

    public override async Task<UserSupplementsListResponse> GetMySupplements(Empty request, ServerCallContext context)
    {
        var userId = context.GetUserId();
        var supplements = await _service.GetUserSupplementsAsync(userId, context.CancellationToken);
        var response = new UserSupplementsListResponse();
        response.Supplements.AddRange(supplements.Select(ToSuppResponse));
        return response;
    }

    private static UserMedicationResponse ToMedResponse(UserMedicationDto dto)
    {
        var r = new UserMedicationResponse
        {
            Id = dto.Id.ToString(),
            MedicationId = dto.MedicationId.ToString(),
            MedicationTradeName = dto.MedicationTradeName,
            MedicationInn = dto.MedicationINN,
            StartDate = Timestamp.FromDateTime(DateTime.SpecifyKind(dto.StartDate, DateTimeKind.Utc)),
            IsActive = dto.IsActive
        };
        if (dto.EndDate.HasValue)
            r.EndDate = Timestamp.FromDateTime(DateTime.SpecifyKind(dto.EndDate.Value, DateTimeKind.Utc));
        return r;
    }

    private static UserSupplementResponse ToSuppResponse(UserSupplementDto dto)
    {
        var r = new UserSupplementResponse
        {
            Id = dto.Id.ToString(),
            SupplementId = dto.SupplementId.ToString(),
            SupplementName = dto.SupplementName,
            StartDate = Timestamp.FromDateTime(DateTime.SpecifyKind(dto.StartDate, DateTimeKind.Utc)),
            IsActive = dto.IsActive
        };
        if (dto.EndDate.HasValue)
            r.EndDate = Timestamp.FromDateTime(DateTime.SpecifyKind(dto.EndDate.Value, DateTimeKind.Utc));
        return r;
    }
}