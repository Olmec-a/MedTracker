using FluentAssertions;
using MedTracker.Application.Interfaces;
using MedTracker.Domain.Entities;
using MedTracker.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MedTracker.Tests.Outbox;

public class OutboxJobTests
{
    private readonly IOutboxRepository _outbox = Substitute.For<IOutboxRepository>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();

    private OutboxJob CreateSut(OutboxOptions? options = null) => new(
        _outbox,
        _emailSender,
        Options.Create(options ?? new OutboxOptions
        {
            BatchSize = 20,
            LockDurationSeconds = 60,
            BaseRetryDelaySeconds = 30,
            CleanupOlderThanHours = 168
        }),
        NullLogger<OutboxJob>.Instance);

    [Fact]
    public async Task ProcessBatch_EmptyBatch_DoesNothing()
    {
        _outbox.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<OutboxMessage>());

        await CreateSut().ProcessBatchAsync();

        await _emailSender.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_SuccessfulSend_MarksProcessed()
    {
        var msg = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            ToAddress = "ok@test.com",
            Subject = "Hi",
            BodyHtml = "<p>hi</p>"
        };
        _outbox.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<OutboxMessage> { msg });

        await CreateSut().ProcessBatchAsync();

        await _emailSender.Received(1).SendAsync(
            "ok@test.com", "Hi", "<p>hi</p>", Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _outbox.Received(1).MarkProcessedAsync(msg.Id, Arg.Any<CancellationToken>());
        await _outbox.DidNotReceive().MarkFailedAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_SendThrows_MarksFailedWithExponentialBackoff()
    {
        var msg = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            ToAddress = "fail@test.com",
            Subject = "X",
            BodyHtml = "<p/>",
            RetryCount = 0
        };
        _outbox.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<OutboxMessage> { msg });
        _emailSender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("SendGrid 401"));

        await CreateSut().ProcessBatchAsync();

        // base * 2^0 = 30 * 1 = 30s для retry 0
        await _outbox.Received(1).MarkFailedAsync(
            msg.Id,
            Arg.Is<string>(e => e.Contains("SendGrid 401")),
            TimeSpan.FromSeconds(30),
            Arg.Any<CancellationToken>());
        await _outbox.DidNotReceive().MarkProcessedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 60)]
    [InlineData(2, 120)]
    [InlineData(3, 240)]
    [InlineData(4, 480)]
    public async Task ProcessBatch_BackoffGrowsExponentially(int retryCount, int expectedDelaySeconds)
    {
        var msg = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            ToAddress = "x@x.com",
            Subject = "X",
            BodyHtml = "<p/>",
            RetryCount = retryCount
        };
        _outbox.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<OutboxMessage> { msg });
        _emailSender.SendAsync(default!, default!, default!, default, default)
            .ThrowsForAnyArgs(new Exception("boom"));

        await CreateSut().ProcessBatchAsync();

        await _outbox.Received(1).MarkFailedAsync(
            msg.Id, Arg.Any<string>(),
            TimeSpan.FromSeconds(expectedDelaySeconds),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_OneFailureDoesNotStopOthers()
    {
        var msg1 = new OutboxMessage { Id = Guid.NewGuid(), ToAddress = "a@x.com", Subject = "A", BodyHtml = "<p/>" };
        var msg2 = new OutboxMessage { Id = Guid.NewGuid(), ToAddress = "b@x.com", Subject = "B", BodyHtml = "<p/>" };
        _outbox.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<OutboxMessage> { msg1, msg2 });

        _emailSender
            .When(x => x.SendAsync("a@x.com", Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>()))
            .Throw(new Exception("a failed"));

        await CreateSut().ProcessBatchAsync();

        await _outbox.Received(1).MarkFailedAsync(msg1.Id, Arg.Any<string>(),
            Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        await _outbox.Received(1).MarkProcessedAsync(msg2.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CleanupOldProcessed_CallsRepoWithConfiguredTtl()
    {
        var options = new OutboxOptions { CleanupOlderThanHours = 48 };
        _outbox.DeleteOldProcessedAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(5);

        await CreateSut(options).CleanupOldProcessedAsync();

        await _outbox.Received(1).DeleteOldProcessedAsync(
            TimeSpan.FromHours(48), Arg.Any<CancellationToken>());
    }
}