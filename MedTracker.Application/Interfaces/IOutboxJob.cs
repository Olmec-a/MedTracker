namespace MedTracker.Application.Interfaces;

/// <summary>
/// Outbox-воркер как сервис. Hangfire дёргает ProcessBatchAsync() по cron.
/// </summary>
public interface IOutboxJob
{
    Task ProcessBatchAsync(CancellationToken ct = default);
    Task CleanupOldProcessedAsync(CancellationToken ct = default);
}