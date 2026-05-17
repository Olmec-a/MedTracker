using FluentAssertions;
using MedTracker.Domain.Entities;
using MedTracker.Domain.Enums;
using MedTracker.Infrastructure.Services;
using MedTracker.IntegrationTests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedTracker.IntegrationTests.Cleanup;

[Collection("Integration")]
public class CleanupServiceTests : IAsyncLifetime
{
    private readonly IntegrationTestFixture _fixture;

    public CleanupServiceTests(IntegrationTestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CleanupExpiredRefreshTokens_DeletesExpiredAndRevoked_KeepsValid()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "owner@test.com",
            PasswordHash = "hash",
            FullName = "Owner",
            Age = 30,
            Role = UserRole.User,
            EmailConfirmed = true
        };

        var validToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = "valid",
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        var expiredToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = "expired",
            ExpiresAt = DateTime.UtcNow.AddDays(-1)
        };
        var revokedToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = "revoked",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsRevoked = true,
            RevokedAt = DateTime.UtcNow.AddHours(-1)
        };

        await using (var ctx = _fixture.CreateDbContext())
        {
            await ctx.Users.AddAsync(user);
            await ctx.RefreshTokens.AddRangeAsync(validToken, expiredToken, revokedToken);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _fixture.CreateDbContext())
        {
            var sut = new CleanupService(ctx, NullLogger<CleanupService>.Instance);
            var deleted = await sut.CleanupExpiredRefreshTokensAsync();
            deleted.Should().Be(2, "expired + revoked");
        }

        await using var verifyCtx = _fixture.CreateDbContext();
        var remaining = verifyCtx.RefreshTokens.ToList();
        remaining.Should().HaveCount(1);
        remaining[0].Token.Should().Be("valid");
    }

    [Fact]
    public async Task CleanupExpiredEmailConfirmationTokens_ClearsExpiredOnly()
    {
        var freshUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "fresh@test.com",
            PasswordHash = "hash",
            FullName = "Fresh",
            Age = 30,
            Role = UserRole.User,
            EmailConfirmed = false,
            EmailConfirmationTokenHash = "FRESH_HASH",
            EmailConfirmationTokenExpiresAt = DateTime.UtcNow.AddHours(12)
        };
        var staleUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "stale@test.com",
            PasswordHash = "hash",
            FullName = "Stale",
            Age = 30,
            Role = UserRole.User,
            EmailConfirmed = false,
            EmailConfirmationTokenHash = "STALE_HASH",
            EmailConfirmationTokenExpiresAt = DateTime.UtcNow.AddHours(-1)
        };

        await using (var ctx = _fixture.CreateDbContext())
        {
            await ctx.Users.AddRangeAsync(freshUser, staleUser);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _fixture.CreateDbContext())
        {
            var sut = new CleanupService(ctx, NullLogger<CleanupService>.Instance);
            var updated = await sut.CleanupExpiredEmailConfirmationTokensAsync();
            updated.Should().Be(1);
        }

        await using var verifyCtx = _fixture.CreateDbContext();
        var fresh = await verifyCtx.Users.FindAsync(freshUser.Id);
        var stale = await verifyCtx.Users.FindAsync(staleUser.Id);

        fresh!.EmailConfirmationTokenHash.Should().Be("FRESH_HASH", "не истёк → не трогаем");
        stale!.EmailConfirmationTokenHash.Should().BeNull("истёк → обнулили");
        stale.EmailConfirmationTokenExpiresAt.Should().BeNull();
    }
}