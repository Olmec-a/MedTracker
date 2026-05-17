using Hangfire;
using MedTracker.Application.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MedTracker.Infrastructure.Jobs;

public class RecurringJobsRegistrar : IHostedService
{
    private readonly IRecurringJobManagerV2 _jobManager;
    private readonly ILogger<RecurringJobsRegistrar> _logger;

    public RecurringJobsRegistrar(
        IRecurringJobManagerV2 jobManager,
        ILogger<RecurringJobsRegistrar> logger)
    {
        _jobManager = jobManager;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _jobManager.AddOrUpdate<IOutboxJob>(
            recurringJobId: "outbox-process-batch",
            methodCall: job => job.ProcessBatchAsync(CancellationToken.None),
            cronExpression: Cron.Minutely(),
            options: new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        _jobManager.AddOrUpdate<IOutboxJob>(
            recurringJobId: "outbox-cleanup-old",
            methodCall: job => job.CleanupOldProcessedAsync(CancellationToken.None),
            cronExpression: Cron.Hourly(),
            options: new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        _jobManager.AddOrUpdate<ICleanupService>(
            recurringJobId: "cleanup-expired-refresh-tokens",
            methodCall: svc => svc.CleanupExpiredRefreshTokensAsync(CancellationToken.None),
            cronExpression: "0 3 * * *",
            options: new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        _jobManager.AddOrUpdate<ICleanupService>(
            recurringJobId: "cleanup-expired-email-confirmation-tokens",
            methodCall: svc => svc.CleanupExpiredEmailConfirmationTokensAsync(CancellationToken.None),
            cronExpression: "0 3 * * *",
            options: new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        _jobManager.AddOrUpdate<ICleanupService>(
            recurringJobId: "cleanup-expired-password-reset-tokens",
            methodCall: svc => svc.CleanupExpiredPasswordResetTokensAsync(CancellationToken.None),
            cronExpression: "0 3 * * *",
            options: new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        _logger.LogInformation("Recurring jobs registered: outbox + cleanup");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}