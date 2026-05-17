using MedTracker.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace MedTracker.Infrastructure.Services;

/// <summary>
/// Outbox-воркер. Hangfire дёргает ProcessBatchAsync() раз в минуту.
///
/// Обновление: при обработке каждого сообщения пушит CorrelationId в LogContext.
/// Если сообщение было создано из gRPC-запроса, в логах OutboxJob будет тот же
/// CorrelationId, что был в исходном запросе. Это позволяет проследить цепочку
/// "Register → 30 секунд позже SendGrid 401 → retry" в одном grep'е.
///
/// Если CorrelationId у сообщения null (system-задачи без HTTP-контекста),
/// используем "outbox-job" как fallback — в логах будет видно "не от запроса".
/// </summary>
public class OutboxJob : IOutboxJob
{
    private readonly IOutboxRepository _outbox;
    private readonly IEmailSender _emailSender;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxJob> _logger;

    public OutboxJob(
        IOutboxRepository outbox,
        IEmailSender emailSender,
        IOptions<OutboxOptions> options,
        ILogger<OutboxJob> logger)
    {
        _outbox = outbox;
        _emailSender = emailSender;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProcessBatchAsync(CancellationToken ct = default)
    {
        var batch = await _outbox.ClaimBatchAsync(
            _options.BatchSize,
            TimeSpan.FromSeconds(_options.LockDurationSeconds),
            ct);

        if (batch.Count == 0) return;

        _logger.LogDebug("Outbox batch claimed: {Count} messages", batch.Count);

        foreach (var msg in batch)
        {
            // Пушим CorrelationId исходного запроса в LogContext.
            // Все логи внутри try/catch для этого сообщения будут содержать тот же ID,
            // что и в исходном API-запросе (Register/RequestPasswordReset/...).
            using (LogContext.PushProperty("CorrelationId", msg.CorrelationId ?? "outbox-job"))
            {
                try
                {
                    await _emailSender.SendAsync(
                        msg.ToAddress, msg.Subject, msg.BodyHtml, msg.BodyPlainText, ct);
                    await _outbox.MarkProcessedAsync(msg.Id, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Exponential backoff: 30s, 60s, 120s, 240s, 480s
                    var delay = TimeSpan.FromSeconds(
                        _options.BaseRetryDelaySeconds * Math.Pow(2, msg.RetryCount));
                    _logger.LogWarning(ex,
                        "Outbox send failed for {MessageId} (retry {Retry}). Next attempt in {Delay}s",
                        msg.Id, msg.RetryCount + 1, delay.TotalSeconds);
                    await _outbox.MarkFailedAsync(msg.Id, ex.Message, delay, ct);
                }
            }
        }
    }

    public async Task CleanupOldProcessedAsync(CancellationToken ct = default)
    {
        // Cleanup — система-задача, корреляции с конкретным запросом нет
        using (LogContext.PushProperty("CorrelationId", "outbox-cleanup"))
        {
            var deleted = await _outbox.DeleteOldProcessedAsync(
                TimeSpan.FromHours(_options.CleanupOlderThanHours), ct);
            if (deleted > 0)
                _logger.LogInformation("Outbox cleanup: deleted {Count} old processed messages", deleted);
        }
    }
}