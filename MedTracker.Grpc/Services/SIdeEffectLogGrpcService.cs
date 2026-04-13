using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MedTracker.Application.DTOs;
using MedTracker.Application.Interfaces;
using MedTracker.Grpc.Interceptors;
using MedTracker.Grpc.Protos;

namespace MedTracker.Grpc.Services;

public class SideEffectLogGrpcService : SideEffectLogService.SideEffectLogServiceBase
{
    private readonly ISideEffectLogService _service;

    public SideEffectLogGrpcService(ISideEffectLogService service)
    {
        _service = service;
    }

    public override async Task<SideEffectLogResponse> LogSideEffect(LogSideEffectRequest request, ServerCallContext context)
    {
        var userId = context.GetUserId();
        var dto = new CreateSideEffectLogDto(
            Guid.Parse(request.SideEffectId),
            request.Date.ToDateTime(),
            (Domain.Enums.SideEffectIntensity)(int)request.Intensity,
            request.Comment?.Value);
        var result = await _service.LogSideEffectAsync(userId, dto, context.CancellationToken);
        return ToResponse(result);
    }

    public override async Task<SideEffectLogsListResponse> GetMySideEffectLogs(SideEffectLogsQuery request, ServerCallContext context)
    {
        var userId = context.GetUserId();
        DateTime? from = request.DateRange?.From?.ToDateTime();
        DateTime? to = request.DateRange?.To?.ToDateTime();
        int page = request.Pagination?.Page ?? 1;
        int pageSize = request.Pagination?.PageSize ?? 20;

        var result = await _service.GetLogsAsync(userId, from, to, page, pageSize, context.CancellationToken);
        var response = new SideEffectLogsListResponse { TotalCount = result.TotalCount };
        response.Logs.AddRange(result.Items.Select(ToResponse));
        return response;
    }

    public override async Task<Empty> DeleteSideEffectLog(DeleteLogRequest request, ServerCallContext context)
    {
        var userId = context.GetUserId();
        await _service.DeleteLogAsync(userId, Guid.Parse(request.LogId), context.CancellationToken);
        return new Empty();
    }

    private static SideEffectLogResponse ToResponse(SideEffectLogDto dto) => new()
    {
        Id = dto.Id.ToString(),
        SideEffectId = dto.SideEffectId.ToString(),
        SideEffectName = dto.SideEffectName,
        Date = Timestamp.FromDateTime(DateTime.SpecifyKind(dto.Date, DateTimeKind.Utc)),
        Intensity = (Protos.SideEffectIntensity)(int)dto.Intensity,
        Comment = dto.Comment ?? string.Empty
    };
}