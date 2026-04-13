using FluentValidation;
using Grpc.Core;
using Grpc.Core.Interceptors;
using MedTracker.Domain.Exceptions;

namespace MedTracker.Grpc.Interceptors;

public class ExceptionInterceptor : Interceptor
{
    private readonly ILogger<ExceptionInterceptor> _logger;

    public ExceptionInterceptor(ILogger<ExceptionInterceptor> logger)
    {
        _logger = logger;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (RpcException)
        {
            throw; // Already a gRPC exception, let it pass
        }
        catch (Exception ex)
        {
            throw MapToRpcException(ex);
        }
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(requestStream, context);
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw MapToRpcException(ex);
        }
    }

    private RpcException MapToRpcException(Exception ex)
    {
        return ex switch
        {
            NotFoundException nfe =>
                new RpcException(new Status(StatusCode.NotFound, nfe.Message)),

            DomainValidationException dve =>
                new RpcException(new Status(StatusCode.InvalidArgument,
                    string.Join("; ", dve.Errors.SelectMany(e => e.Value)))),

            ValidationException ve =>
                new RpcException(new Status(StatusCode.InvalidArgument,
                    string.Join("; ", ve.Errors.Select(e => e.ErrorMessage)))),

            UnauthorizedException ue =>
                new RpcException(new Status(StatusCode.Unauthenticated, ue.Message)),

            ForbiddenException fe =>
                new RpcException(new Status(StatusCode.PermissionDenied, fe.Message)),

            DuplicateException de =>
                new RpcException(new Status(StatusCode.AlreadyExists, de.Message)),

            ExcelImportException ie =>
                new RpcException(new Status(StatusCode.InvalidArgument, ie.Message)),

            _ => LogAndCreateInternal(ex)
        };
    }

    private RpcException LogAndCreateInternal(Exception ex)
    {
        _logger.LogError(ex, "Unhandled exception in gRPC call");
        return new RpcException(new Status(StatusCode.Internal, "An internal error occurred."));
    }
}