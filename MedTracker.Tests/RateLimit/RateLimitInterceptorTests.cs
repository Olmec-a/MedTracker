using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using MedTracker.Application.Interfaces;
using MedTracker.Grpc.Interceptors;
using MedTracker.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MedTracker.Tests.RateLimit;

public class RateLimitInterceptorTests
{
    private readonly IRateLimiter _rateLimiter = Substitute.For<IRateLimiter>();
    private readonly RateLimitInterceptor _sut;

    public RateLimitInterceptorTests()
    {
        _sut = new RateLimitInterceptor(_rateLimiter, NullLogger<RateLimitInterceptor>.Instance);
    }

    [Fact]
    public async Task UnaryServerHandler_MethodNotInRules_PassesThrough()
    {
        var ctx = FakeServerCallContext.Create("/medtracker.UserProfileService/GetProfile");
        var continuationCalled = false;

        UnaryServerMethod<string, string> continuation = (_, _) =>
        {
            continuationCalled = true;
            return Task.FromResult("ok");
        };

        var result = await _sut.UnaryServerHandler("req", ctx, continuation);

        result.Should().Be("ok");
        continuationCalled.Should().BeTrue();
        await _rateLimiter.DidNotReceive().CheckAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnaryServerHandler_RateLimitAllowed_PassesThrough()
    {
        var ctx = FakeServerCallContext.Create("/medtracker.AuthService/Login");
        _rateLimiter.CheckAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult(true, 1, 5, TimeSpan.FromMinutes(1)));

        UnaryServerMethod<string, string> continuation = (_, _) => Task.FromResult("ok");

        var result = await _sut.UnaryServerHandler("req", ctx, continuation);

        result.Should().Be("ok");
    }

    [Fact]
    public async Task UnaryServerHandler_RateLimitExceeded_ThrowsResourceExhausted()
    {
        var ctx = FakeServerCallContext.Create("/medtracker.AuthService/Login");
        _rateLimiter.CheckAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult(false, 6, 5, TimeSpan.FromSeconds(45)));

        UnaryServerMethod<string, string> continuation = (_, _) => Task.FromResult("ok");

        var act = () => _sut.UnaryServerHandler("req", ctx, continuation);

        await act.Should().ThrowAsync<RpcException>()
            .Where(ex => ex.StatusCode == StatusCode.ResourceExhausted
                         && ex.Status.Detail.Contains("Rate limit exceeded")
                         && ex.Trailers.Any(t => t.Key == "retry-after-seconds" && t.Value == "45"));
    }
}