using Grpc.Core;
using Grpc.Core.Interceptors;
using Serilog.Context;

namespace MedTracker.Grpc.Interceptors;

/// <summary>
/// Каждому входящему gRPC-запросу присваивает CorrelationId.
///
/// Источники (в порядке приоритета):
///   1. Header "x-correlation-id" от клиента — для распределённой трассировки
///   2. Сгенерированный GUID, если клиент не прислал
///
/// Дальше ID:
///   - Кладётся в Serilog LogContext → автоматически попадает в каждый лог-event
///   - Кладётся в context.UserState → доступен в gRPC-сервисах
///   - Возвращается клиенту в trailers, чтобы он мог зацепить за свои логи
///
/// Должен быть зарегистрирован ПЕРВЫМ среди интерцепторов, иначе ID не попадёт
/// в логи остальных interceptor'ов.
/// </summary>
public class CorrelationIdInterceptor : Interceptor
{
    public const string HeaderName = "x-correlation-id";
    public const string UserStateKey = "CorrelationId";

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var correlationId = ExtractOrGenerate(context);

        // Логи в Serilog внутри этого scope автоматически содержат CorrelationId.
        // PushProperty корректно очищается в Dispose'е, даже если внутри throw.
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            context.UserState[UserStateKey] = correlationId;

            // Возвращаем клиенту тот же ID — он может его сохранить и приложить к багу
            await context.WriteResponseHeadersAsync(new Metadata
            {
                { HeaderName, correlationId }
            });

            return await continuation(request, context);
        }
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        var correlationId = ExtractOrGenerate(context);
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            context.UserState[UserStateKey] = correlationId;
            await context.WriteResponseHeadersAsync(new Metadata { { HeaderName, correlationId } });
            return await continuation(requestStream, context);
        }
    }

    private static string ExtractOrGenerate(ServerCallContext context)
    {
        var fromHeader = context.RequestHeaders.GetValue(HeaderName);
        if (!string.IsNullOrWhiteSpace(fromHeader) && fromHeader.Length <= 128)
            return fromHeader;
        return Guid.NewGuid().ToString("N")[..16]; // короткий 16-символьный ID
    }
}

public static class ServerCallContextCorrelationExtensions
{
    public static string? GetCorrelationId(this ServerCallContext context)
        => context.UserState.TryGetValue(CorrelationIdInterceptor.UserStateKey, out var v) && v is string s
            ? s : null;
}