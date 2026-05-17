using FluentAssertions;
using MedTracker.Infrastructure.Services;
using MedTracker.IntegrationTests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedTracker.IntegrationTests.RateLimit;

[Collection("Integration")]
public class RedisRateLimiterTests : IAsyncLifetime
{
    private readonly IntegrationTestFixture _fixture;
    private readonly RedisRateLimiter _sut;

    public RedisRateLimiterTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _sut = new RedisRateLimiter(_fixture.Redis, NullLogger<RedisRateLimiter>.Instance);
    }

    public Task InitializeAsync() => _fixture.ResetRedisAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CheckAsync_FirstRequest_AllowedAndCountIs1()
    {
        var result = await _sut.CheckAsync("test-key-1", limit: 5, window: TimeSpan.FromMinutes(1));

        result.Allowed.Should().BeTrue();
        result.CurrentCount.Should().Be(1);
        result.Limit.Should().Be(5);
        result.ResetAfter.Should().BeCloseTo(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task CheckAsync_RequestsUpToLimit_AllAllowed()
    {
        for (int i = 1; i <= 5; i++)
        {
            var result = await _sut.CheckAsync("test-key-2", limit: 5, window: TimeSpan.FromMinutes(1));
            result.Allowed.Should().BeTrue($"запрос {i} в пределах лимита");
            result.CurrentCount.Should().Be(i);
        }
    }

    [Fact]
    public async Task CheckAsync_OverLimit_NotAllowed()
    {
        // Жмём 5 раз — все ОК
        for (int i = 0; i < 5; i++)
            await _sut.CheckAsync("test-key-3", limit: 5, window: TimeSpan.FromMinutes(1));

        // 6-й — должен быть заблокирован
        var result = await _sut.CheckAsync("test-key-3", limit: 5, window: TimeSpan.FromMinutes(1));
        result.Allowed.Should().BeFalse();
        result.CurrentCount.Should().Be(6);
    }

    [Fact]
    public async Task CheckAsync_DifferentKeys_DoNotAffectEachOther()
    {
        for (int i = 0; i < 5; i++)
            await _sut.CheckAsync("isolated-key-A", limit: 5, window: TimeSpan.FromMinutes(1));

        // Лимит для key-A исчерпан, но для key-B всё ещё свежо
        var resultB = await _sut.CheckAsync("isolated-key-B", limit: 5, window: TimeSpan.FromMinutes(1));
        resultB.Allowed.Should().BeTrue();
        resultB.CurrentCount.Should().Be(1);
    }

    [Fact]
    public async Task CheckAsync_Concurrent_AtomicCount()
    {
        // Жёсткий тест атомарности: 100 параллельных запросов к одному ключу.
        // Должно быть ровно 100 в счётчике, и ровно (limit) Allowed-результатов.
        const int concurrentRequests = 100;
        const int limit = 30;

        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => _sut.CheckAsync("atomic-test", limit, TimeSpan.FromMinutes(1)))
            .ToList();

        var results = await Task.WhenAll(tasks);

        results.Length.Should().Be(concurrentRequests);
        results.Count(r => r.Allowed).Should().Be(limit, $"ровно {limit} запросов должны быть allowed");
        results.Count(r => !r.Allowed).Should().Be(concurrentRequests - limit);

        // CurrentCount у последнего инкрементированного должен быть = 100
        results.Max(r => r.CurrentCount).Should().Be(concurrentRequests);
    }

    [Fact]
    public async Task CheckAsync_AfterWindowExpires_CountResets()
    {
        // Короткое окно — 2 секунды
        var window = TimeSpan.FromSeconds(2);

        // Забили лимит
        for (int i = 0; i < 3; i++)
            await _sut.CheckAsync("ttl-test", limit: 3, window);

        // Сразу после — заблокировано
        var blocked = await _sut.CheckAsync("ttl-test", limit: 3, window);
        blocked.Allowed.Should().BeFalse();

        // Ждём истечения окна (плюс запас)
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Теперь снова разрешено
        var afterReset = await _sut.CheckAsync("ttl-test", limit: 3, window);
        afterReset.Allowed.Should().BeTrue();
        afterReset.CurrentCount.Should().Be(1, "счётчик начался заново");
    }
}