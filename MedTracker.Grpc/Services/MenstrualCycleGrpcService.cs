using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MedTracker.Application.DTOs;
using MedTracker.Application.Interfaces;
using MedTracker.Domain.Enums;
using MedTracker.Grpc.Interceptors;
using MedTracker.Grpc.Protos;

namespace MedTracker.Grpc.Services;

public class MenstrualCycleGrpcService : MenstrualCycleService.MenstrualCycleServiceBase
{
    private readonly IMenstrualCycleService _service;

    public MenstrualCycleGrpcService(IMenstrualCycleService service)
    {
        _service = service;
    }

    public override async Task<CycleEntryResponse> AddCycleEntry(AddCycleEntryRequest request, ServerCallContext context)
    {
        var userId = context.GetUserId();
        var dto = new CreateCycleEntryDto(
            request.StartDate.ToDateTime(),
            request.EndDate?.ToDateTime(),
            (CycleIntensity)(int)request.Intensity,
            request.Symptoms.ToList(),
            request.Notes?.Value);
        var result = await _service.AddEntryAsync(userId, dto, context.CancellationToken);
        return ToResponse(result);
    }

    public override async Task<CycleEntryResponse> UpdateCycleEntry(UpdateCycleEntryRequest request, ServerCallContext context)
    {
        var userId = context.GetUserId();
        var dto = new UpdateCycleEntryDto(
            Guid.Parse(request.Id),
            request.StartDate.ToDateTime(),
            request.EndDate?.ToDateTime(),
            (CycleIntensity)(int)request.Intensity,
            request.Symptoms.ToList(),
            request.Notes?.Value);
        var result = await _service.UpdateEntryAsync(userId, dto, context.CancellationToken);
        return ToResponse(result);
    }

    public override async Task<CycleEntriesListResponse> GetCycleHistory(CycleHistoryQuery request, ServerCallContext context)
    {
        var userId = context.GetUserId();
        DateTime? from = request.DateRange?.From?.ToDateTime();
        DateTime? to = request.DateRange?.To?.ToDateTime();
        int page = request.Pagination?.Page ?? 1;
        int pageSize = request.Pagination?.PageSize ?? 20;

        var result = await _service.GetHistoryAsync(userId, from, to, page, pageSize, context.CancellationToken);
        var response = new CycleEntriesListResponse { TotalCount = result.TotalCount };
        response.Entries.AddRange(result.Items.Select(ToResponse));
        return response;
    }

    public override async Task<Empty> DeleteCycleEntry(DeleteCycleEntryRequest request, ServerCallContext context)
    {
        var userId = context.GetUserId();
        await _service.DeleteEntryAsync(userId, Guid.Parse(request.Id), context.CancellationToken);
        return new Empty();
    }

    private static CycleEntryResponse ToResponse(MenstrualCycleDto dto)
    {
        var r = new CycleEntryResponse
        {
            Id = dto.Id.ToString(),
            StartDate = Timestamp.FromDateTime(DateTime.SpecifyKind(dto.StartDate, DateTimeKind.Utc)),
            Intensity = (Protos.CycleIntensity)(int)dto.Intensity,
            Notes = dto.Notes ?? string.Empty
        };
        if (dto.EndDate.HasValue)
            r.EndDate = Timestamp.FromDateTime(DateTime.SpecifyKind(dto.EndDate.Value, DateTimeKind.Utc));
        r.Symptoms.AddRange(dto.Symptoms);
        return r;
    }
}