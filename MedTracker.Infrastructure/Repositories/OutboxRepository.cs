using MedTracker.Application.Interfaces;
using MedTracker.Domain.Entities;
using MedTracker.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MedTracker.Infrastructure.Repositories;

/// <summary>
/// Outbox-репозиторий, безопасный для multi-replica.
///
/// ClaimBatchAsync использует Postgres FOR UPDATE SKIP LOCKED:
/// - Транзакция блокирует выбранные строки → другие реплики их не увидят (SKIP LOCKED)
/// - Внутри той же транзакции выставляем LockedUntil/LockToken
/// - После COMMIT row-level lock отпускается, но LockedUntil "защищает" сообщение
///   следующие N секунд — даже если этот воркер умер, никто не возьмёт сообщение
///   до истечения lock'а.
/// </summary>
public class OutboxRepository : IOutboxRepository
{
    private const int MaxRetryCount = 5;
    private readonly AppDbContext _db;

    public OutboxRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(OutboxMessage message, CancellationToken ct = default)
        => await _db.OutboxMessages.AddAsync(message, ct);

    public async Task<List<OutboxMessage>> ClaimBatchAsync(
        int batchSize, TimeSpan lockDuration, CancellationToken ct = default)
    {
        var lockUntil = DateTime.UtcNow.Add(lockDuration);
        var newToken = Guid.NewGuid();

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // FromSqlInterpolated с FOR UPDATE SKIP LOCKED.
            // EF возвращает tracked entities — изменения лучше делать осторожно,
            // но мы выходим из этого scope сразу после Commit и контекст scoped.
            var claimed = await _db.OutboxMessages
                .FromSqlInterpolated($"""
                    SELECT * FROM "OutboxMessages"
                    WHERE "ProcessedAt" IS NULL
                      AND "RetryCount" < {MaxRetryCount}
                      AND ("LockedUntil" IS NULL OR "LockedUntil" < (NOW() AT TIME ZONE 'UTC'))
                      AND ("NextRetryAt" IS NULL OR "NextRetryAt" <= (NOW() AT TIME ZONE 'UTC'))
                    ORDER BY "CreatedAt"
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .AsTracking()
                .ToListAsync(ct);

            if (claimed.Count == 0)
            {
                await transaction.CommitAsync(ct);
                return new List<OutboxMessage>();
            }

            foreach (var msg in claimed)
            {
                msg.LockToken = newToken;
                msg.LockedUntil = lockUntil;
            }

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return claimed;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
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