using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace MedTracker.Grpc.Interceptors;

/// <summary>
/// Блокирует большие payload для обычных методов; ImportMedicationData может быть большим.
/// gRPC сам режет по MaxReceiveMessageSize; этот interceptor добавляет per-method лимиты.
/// </summary>
public class RequestSizeInterceptor : Interceptor
{
    private const int DefaultLimitBytes = 1 * 1024 * 1024; // 1 MB

    // Методы с большим лимитом (bytes)
    private static readonly Dictionary<string, int> MethodLimits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/medtracker.AdminService/ImportMedicationData"] = 50 * 1024 * 1024 // 50 MB
    };

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        if (request is IMessage message)
        {
            var limit = MethodLimits.TryGetValue(context.Method, out var perMethod) ? perMethod : DefaultLimitBytes;
            var size = message.CalculateSize();

            if (size > limit)
                throw new RpcException(new Status(
                    StatusCode.ResourceExhausted,
                    $"Request size ({size} bytes) exceeds limit ({limit} bytes) for this method."));
        }

        return await continuation(request, context);
    }
}