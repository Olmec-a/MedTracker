using MedTracker.Application.Interfaces;
using Grpc.AspNetCore.Server;
using MedTracker.Grpc.Interceptors;

namespace MedTracker.Grpc;

public class HttpContextCorrelationIdAccessor : ICorrelationIdAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCorrelationIdAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? Current
    {
        get
        {
            var httpCtx = _httpContextAccessor.HttpContext;
            if (httpCtx == null) return null;

            // gRPC хранит ServerCallContext в HttpContext.Features
            var serverCallCtx = httpCtx.Features.Get<IServerCallContextFeature>()?.ServerCallContext;
            return serverCallCtx?.GetCorrelationId();
        }
    }
}