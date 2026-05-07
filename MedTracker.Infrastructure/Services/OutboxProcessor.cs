using MedTracker.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedTracker.Infrastructure.Services;

public class OutboxOptions
{
    public int BatchSize { get; set; } = 20;
    public int PollIntervalSeconds { get; set; } = 10;
    public int LockDurationSeconds { get; set; } = 60;

    /// <summary>Базовая задержка для exponential backoff между retry.</summary>
    public int BaseRetryDelaySeconds { get; set; } = 30;

    /// <summary>Очистка обработанных сообщений старше N часов.</summary>
    public int CleanupOlderThanHours { get; set; } = 168; // 7 дней
    public int CleanupIntervalMinutes { get; set; } = 60;
}

/// <summary>
/// Фоновый воркер, который читает Outbox и отправляет письма через IEmailSender.
/// Когда подключим Hangfire — этот класс уйдёт под него (Recurring Job).
/// Сейчас работает как самостоятельный BackgroundService.
/// </summary>
public class OutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxProcessor> _logger;
    private DateTime _lastCleanup = DateTime.MinValue;

    public OutboxProcessor(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxOptions> options,
        ILogger<OutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxProcessor started. Poll every {Interval}s, batch {Batch}",
            _options.PollIntervalSeconds, _options.BatchSize);

        var pollDelay = TimeSpan.FromSeconds(_options.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
                await TryCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OutboxProcessor batch failed; will retry");
            }

            try { await Task.Delay(pollDelay, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("OutboxProcessor stopped");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var batch = await outbox.ClaimBatchAsync(
            _options.BatchSize,
            TimeSpan.FromSeconds(_options.LockDurationSeconds),
            ct);

        if (batch.Count == 0) return;

        _logger.LogDebug("Outbox batch claimed: {Count} messages", batch.Count);

        foreach (var msg in batch)
        {
            try
            {
                await emailSender.SendAsync(msg.ToAddress, msg.Subject, msg.BodyHtml, msg.BodyPlainText, ct);
                await outbox.MarkProcessedAsync(msg.Id, ct);
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
                await outbox.MarkFailedAsync(msg.Id, ex.Message, delay, ct);
            }
        }
    }

    private async Task TryCleanupAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - _lastCleanup < TimeSpan.FromMinutes(_options.CleanupIntervalMinutes))
            return;

        _lastCleanup = DateTime.UtcNow;
        using var scope = _scopeFactory.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();

        var deleted = await outbox.DeleteOldProcessedAsync(
            TimeSpan.FromHours(_options.CleanupOlderThanHours), ct);

        if (deleted > 0)
            _logger.LogInformation("Outbox cleanup: deleted {Count} old processed messages", deleted);
    }
}