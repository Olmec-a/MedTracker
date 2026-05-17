namespace MedTracker.Infrastructure.Services;

public class OutboxOptions
{
    public int BatchSize { get; set; } = 20;
    public int PollIntervalSeconds { get; set; } = 10;
    public int LockDurationSeconds { get; set; } = 60;
    public int BaseRetryDelaySeconds { get; set; } = 30;
    public int CleanupOlderThanHours { get; set; } = 168;
    public int CleanupIntervalMinutes { get; set; } = 60;
}