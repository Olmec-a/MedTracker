using MedTracker.Application.Interfaces;
using MedTracker.Domain.Entities;
using MedTracker.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MedTracker.Infrastructure.Repositories;

/// <summary>
/// Outbox-репозиторий с safe-claim семантикой через optimistic locking.
/// На несколько реплик нужен FOR UPDATE SKIP LOCKED — пока используем
/// LockedUntil + LockToken (worker safe).
/// </summary>
public class OutboxRepository : IOutboxRepository
{
    private const int MaxRetryCount = 5;
    private readonly AppDbContext _db;

    public OutboxRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(OutboxMessage message, CancellationToken ct = default)
        => await _db.OutboxMessages.AddAsync(message, ct);

    public async Task<List<OutboxMessage>> ClaimBatchAsync(int batchSize, TimeSpan lockDuration, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var lockUntil = now.Add(lockDuration);

        // Берём кандидатов: не обработанные, либо locker истёк, либо ещё не пришло время retry
        var candidates = await _db.OutboxMessages
            .Where(m => m.ProcessedAt == null
                        && m.RetryCount < MaxRetryCount
                        && (m.LockedUntil == null || m.LockedUntil < now)
                        && (m.NextRetryAt == null || m.NextRetryAt <= now))
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return new List<OutboxMessage>();

        // Атомарно "захватываем" — обновляем токен + LockedUntil.
        // Если другая реплика уже захватила — её LockToken будет другой,
        // и наш UPDATE WHERE LockToken=oldToken не сработает.
        var claimed = new List<OutboxMessage>();
        foreach (var msg in candidates)
        {
            var oldToken = msg.LockToken;
            var newToken = Guid.NewGuid();

            var rows = await _db.OutboxMessages
                .Where(m => m.Id == msg.Id && m.LockToken == oldToken)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.LockToken, newToken)
                    .SetProperty(m => m.LockedUntil, lockUntil), ct);

            if (rows == 1)
            {
                msg.LockToken = newToken;
                msg.LockedUntil = lockUntil;
                claimed.Add(msg);
            }
        }
        return claimed;
    }

    public async Task MarkProcessedAsync(Guid id, CancellationToken ct = default)
    {
        await _db.OutboxMessages
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.ProcessedAt, DateTime.UtcNow)
                .SetProperty(m => m.LockedUntil, (DateTime?)null)
                .SetProperty(m => m.LastError, (string?)null), ct);
    }

    public async Task MarkFailedAsync(Guid id, string error, TimeSpan nextRetryDelay, CancellationToken ct = default)
    {
        var nextRetry = DateTime.UtcNow.Add(nextRetryDelay);
        // Truncate error до разумной длины
        var truncated = error.Length > 2000 ? error[..2000] : error;

        await _db.OutboxMessages
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.RetryCount, m => m.RetryCount + 1)
                .SetProperty(m => m.NextRetryAt, nextRetry)
                .SetProperty(m => m.LockedUntil, (DateTime?)null)
                .SetProperty(m => m.LastError, truncated), ct);
    }

    public async Task<int> DeleteOldProcessedAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.Subtract(olderThan);
        return await _db.OutboxMessages
            .Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}