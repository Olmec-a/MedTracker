using FluentAssertions;
using MedTracker.Domain.Entities;
using MedTracker.Infrastructure.Repositories;
using MedTracker.IntegrationTests.Fixtures;
using Xunit;

namespace MedTracker.IntegrationTests.Outbox;

[Collection("Integration")]
public class OutboxRepositoryTests : IAsyncLifetime
{
    private readonly IntegrationTestFixture _fixture;

    public OutboxRepositoryTests(IntegrationTestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ClaimBatch_TwoConcurrentWorkers_DoNotClaimSameMessage()
    {
        // SETUP: вставили 10 сообщений в outbox
        await using (var ctx = _fixture.CreateDbContext())
        {
            for (int i = 0; i < 10; i++)
            {
                await ctx.OutboxMessages.AddAsync(new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    ToAddress = $"user{i}@test.com",
                    Subject = $"Subject {i}",
                    BodyHtml = $"<p>Body {i}</p>",
                    BodyPlainText = $"Body {i}"
                });
            }
            await ctx.SaveChangesAsync();
        }

        // ACT: две параллельные claim'ы — каждая со своим DbContext'ом и своей транзакцией
        var lockDuration = TimeSpan.FromSeconds(60);
        var batchTask1 = ClaimBatchInIsolatedContext(5, lockDuration);
        var batchTask2 = ClaimBatchInIsolatedContext(5, lockDuration);

        var results = await Task.WhenAll(batchTask1, batchTask2);
        var batch1 = results[0];
        var batch2 = results[1];

        // ASSERT: оба клайма успешны, и НИ ОДНО сообщение не оказалось в обоих батчах
        batch1.Should().HaveCount(5, "первый воркер взял 5 сообщений");
        batch2.Should().HaveCount(5, "второй воркер взял оставшиеся 5");

        var batch1Ids = batch1.Select(m => m.Id).ToHashSet();
        var batch2Ids = batch2.Select(m => m.Id).ToHashSet();
        var intersection = batch1Ids.Intersect(batch2Ids).ToList();

        intersection.Should().BeEmpty(
            "FOR UPDATE SKIP LOCKED гарантирует, что одно сообщение не возьмут оба воркера");

        // Все 10 сообщений в БД помечены как locked
        await using var verifyCtx = _fixture.CreateDbContext();
        var lockedCount = verifyCtx.OutboxMessages.Count(m => m.LockedUntil != null);
        lockedCount.Should().Be(10);
    }

    [Fact]
    public async Task MarkProcessed_ClearsLockAndSetsProcessedAt()
    {
        var messageId = Guid.NewGuid();
        await using (var ctx = _fixture.CreateDbContext())
        {
            await ctx.OutboxMessages.AddAsync(new OutboxMessage
            {
                Id = messageId,
                ToAddress = "ok@test.com",
                Subject = "X",
                BodyHtml = "<p/>",
                LockedUntil = DateTime.UtcNow.AddMinutes(1)
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _fixture.CreateDbContext())
        {
            var repo = new OutboxRepository(ctx);
            await repo.MarkProcessedAsync(messageId);
        }

        await using var verifyCtx = _fixture.CreateDbContext();
        var msg = await verifyCtx.OutboxMessages.FindAsync(messageId);
        msg.Should().NotBeNull();
        msg!.ProcessedAt.Should().NotBeNull();
        msg.LockedUntil.Should().BeNull();
        msg.LastError.Should().BeNull();
    }

    [Fact]
    public async Task MarkFailed_IncrementsRetryCountAndSetsBackoff()
    {
        var messageId = Guid.NewGuid();
        await using (var ctx = _fixture.CreateDbContext())
        {
            await ctx.OutboxMessages.AddAsync(new OutboxMessage
            {
                Id = messageId,
                ToAddress = "fail@test.com",
                Subject = "X",
                BodyHtml = "<p/>",
                RetryCount = 2,
                LockedUntil = DateTime.UtcNow.AddMinutes(1)
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _fixture.CreateDbContext())
        {
            var repo = new OutboxRepository(ctx);
            await repo.MarkFailedAsync(messageId, "SendGrid 401", TimeSpan.FromSeconds(120));
        }

        await using var verifyCtx = _fixture.CreateDbContext();
        var msg = await verifyCtx.OutboxMessages.FindAsync(messageId);
        msg!.RetryCount.Should().Be(3);
        msg.LastError.Should().Be("SendGrid 401");
        msg.NextRetryAt.Should().BeCloseTo(DateTime.UtcNow.AddSeconds(120), TimeSpan.FromSeconds(5));
        msg.LockedUntil.Should().BeNull("после неудачи lock освобождается, чтобы next retry мог взять");
    }

    [Fact]
    public async Task ClaimBatch_RespectsMaxRetryCount_DoesNotClaimDeadMessages()
    {
        // RetryCount=5 — это max в OutboxRepository. Такие сообщения больше не должны браться.
        await using (var ctx = _fixture.CreateDbContext())
        {
            await ctx.OutboxMessages.AddAsync(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                ToAddress = "dead@test.com",
                Subject = "X",
                BodyHtml = "<p/>",
                RetryCount = 5
            });
            await ctx.OutboxMessages.AddAsync(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                ToAddress = "alive@test.com",
                Subject = "X",
                BodyHtml = "<p/>",
                RetryCount = 4
            });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = _fixture.CreateDbContext();
        var repo = new OutboxRepository(ctx2);
        var batch = await repo.ClaimBatchAsync(10, TimeSpan.FromSeconds(60));

        batch.Should().HaveCount(1);
        batch[0].ToAddress.Should().Be("alive@test.com");
    }

    private async Task<List<OutboxMessage>> ClaimBatchInIsolatedContext(int batchSize, TimeSpan lockDuration)
    {
        await using var ctx = _fixture.CreateDbContext();
        var repo = new OutboxRepository(ctx);
        return await repo.ClaimBatchAsync(batchSize, lockDuration);
    }
}