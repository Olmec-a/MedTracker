using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MedTracker.Application.Interfaces;
using MedTracker.Grpc.Interceptors;
using MedTracker.Grpc.Protos;

namespace MedTracker.Grpc.Services;

public class AdminGrpcService : AdminService.AdminServiceBase
{
    private readonly IExcelImportService _importService;
    private readonly ILogger<AdminGrpcService> _logger;

    public AdminGrpcService(IExcelImportService importService, ILogger<AdminGrpcService> logger)
    {
        _importService = importService;
        _logger = logger;
    }

    public override async Task<ImportResultResponse> ImportMedicationData(
        IAsyncStreamReader<ImportChunk> requestStream,
        ServerCallContext context)
    {
        var userId = context.GetUserId();
        var chunks = new List<byte>();
        string fileName = string.Empty;
        string diagnosisName = string.Empty;

        await foreach (var chunk in requestStream.ReadAllAsync(context.CancellationToken))
        {
            if (string.IsNullOrEmpty(fileName))
                fileName = chunk.FileName;
            if (string.IsNullOrEmpty(diagnosisName))
                diagnosisName = chunk.DiagnosisName;

            chunks.AddRange(chunk.ChunkData.ToByteArray());
        }

        if (chunks.Count == 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "No file data received."));

        if (string.IsNullOrWhiteSpace(diagnosisName))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Diagnosis name is required."));

        _logger.LogInformation("Received import file {FileName} ({Size} bytes) for diagnosis {Diagnosis}",
            fileName, chunks.Count, diagnosisName);

        var result = await _importService.ImportAsync(
            chunks.ToArray(), fileName, diagnosisName, userId, context.CancellationToken);

        return new ImportResultResponse
        {
            Success = result.Success,
            MedicationsImported = result.MedicationsImported,
            SupplementsImported = result.SupplementsImported,
            SideEffectsImported = result.SideEffectsImported,
            Message = result.Message
        };
    }

    public override async Task<ImportHistoryResponse> GetImportHistory(ImportHistoryRequest request, ServerCallContext context)
    {
        var records = await _importService.GetImportHistoryAsync(context.CancellationToken);
        var response = new ImportHistoryResponse();
        response.Records.AddRange(records.Select(r => new ImportRecord
        {
            Id = r.Id.ToString(),
            FileName = r.FileName,
            DiagnosisName = r.DiagnosisName,
            RecordsImported = r.RecordsImported,
            ImportedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(r.ImportedAt, DateTimeKind.Utc)),
            ImportedBy = r.ImportedBy
        }));
        return response;
    }
}