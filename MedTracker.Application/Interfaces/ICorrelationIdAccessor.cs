namespace MedTracker.Application.Interfaces;

/// <summary>
/// Абстракция для получения CorrelationId текущего запроса в Application слое.
/// Реализация в Grpc слое читает из ServerCallContext через IHttpContextAccessor.
/// </summary>
public interface ICorrelationIdAccessor
{
    string? Current { get; }
}