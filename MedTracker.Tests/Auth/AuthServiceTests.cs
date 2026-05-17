using FluentAssertions;
using MedTracker.Application.DTOs;
using MedTracker.Domain.Entities;
using MedTracker.Domain.Exceptions;
using MedTracker.Tests.Common;
using MedTracker.Tests.Common.Builders;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MedTracker.Tests.Auth;

public class AuthServiceTests
{
    [Fact]
    public async Task Register_NewEmail_CreatesUserAndEnqueuesConfirmationEmail()
    {
        var setup = new AuthServiceTestSetup();
        setup.UserRepo.ExistsByEmailAsync("new@test.com", Arg.Any<CancellationToken>()).Returns(false);
        setup.PasswordHasher.Hash("StrongPass1").Returns("hashed-password");

        var sut = setup.Build();

        var result = await sut.RegisterAsync(
            new RegisterDto("new@test.com", "StrongPass1", "Test", 25));

        result.AccessToken.Should().Be("test-access-token");
        result.RefreshToken.Should().NotBeNullOrEmpty();

        await setup.UserRepo.Received(1).AddAsync(
            Arg.Is<User>(u =>
                u.Email == "new@test.com"
                && u.PasswordHash == "hashed-password"
                && !u.EmailConfirmed
                && u.EmailConfirmationTokenHash == "HASH-OF-PLAIN-TOKEN"
                && u.EmailConfirmationTokenExpiresAt > DateTime.UtcNow.AddHours(23)),
            Arg.Any<CancellationToken>());

        setup.UrlBuilder.Received(1).BuildEmailConfirmationUrl("new@test.com", "plain-token-xxx");

        await setup.OutboxRepo.Received(1).AddAsync(
            Arg.Is<OutboxMessage>(m => m.ToAddress == "new@test.com"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Register_DuplicateEmail_ThrowsDuplicateException()
    {
        var setup = new AuthServiceTestSetup();
        setup.UserRepo.ExistsByEmailAsync("dup@test.com", Arg.Any<CancellationToken>()).Returns(true);

        var sut = setup.Build();

        var act = () => sut.RegisterAsync(
            new RegisterDto("dup@test.com", "StrongPass1", "X", 25));

        await act.Should().ThrowAsync<DuplicateException>();
        await setup.UserRepo.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_EmailNotConfirmed_ThrowsUnauthorizedAfterValidatingPassword()
    {
        var setup = new AuthServiceTestSetup();
        var user = new UserBuilder()
            .WithEmail("notconfirmed@test.com")
            .WithPasswordHash("hashed")
            .Unconfirmed("HASH", DateTime.UtcNow.AddHours(1))
            .WithFailedAttempts(2)
            .Build();
        setup.UserRepo.GetByEmailAsync("notconfirmed@test.com", Arg.Any<CancellationToken>()).Returns(user);
        setup.PasswordHasher.Verify("CorrectPass1", "hashed").Returns(true);

        var sut = setup.Build();

        var act = () => sut.LoginAsync(new LoginDto("notconfirmed@test.com", "CorrectPass1"));

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("*Email is not confirmed*");
        user.FailedLoginAttempts.Should().Be(0, "правильный пароль сбрасывает счётчик");
    }

    [Fact]
    public async Task Login_WrongPassword_FifthAttempt_LocksAccount()
    {
        var setup = new AuthServiceTestSetup();
        var user = new UserBuilder()
            .WithEmail("brute@test.com")
            .WithPasswordHash("hashed")
            .WithFailedAttempts(4)
            .Build();
        setup.UserRepo.GetByEmailAsync("brute@test.com", Arg.Any<CancellationToken>()).Returns(user);
        setup.PasswordHasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var sut = setup.Build();

        var act = () => sut.LoginAsync(new LoginDto("brute@test.com", "wrong"));

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("*locked for 15 minutes*");

        user.LockoutUntil.Should().NotBeNull();
        user.LockoutUntil!.Value.Should().BeCloseTo(
            DateTime.UtcNow.AddMinutes(15), TimeSpan.FromSeconds(5));
        user.FailedLoginAttempts.Should().Be(0, "после lockout счётчик сбрасывается");
    }

    [Fact]
    public async Task Login_AccountLocked_FailsWithoutCheckingPassword()
    {
        var setup = new AuthServiceTestSetup();
        var user = new UserBuilder()
            .WithEmail("locked@test.com")
            .LockedUntil(DateTime.UtcNow.AddMinutes(10))
            .Build();
        setup.UserRepo.GetByEmailAsync("locked@test.com", Arg.Any<CancellationToken>()).Returns(user);

        var sut = setup.Build();

        var act = () => sut.LoginAsync(new LoginDto("locked@test.com", "any"));

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("*Account is locked*");

        // Dummy-hash всё равно проверяется (timing-safety)
        setup.PasswordHasher.Received(1).Verify("any", Arg.Is<string>(h => h.StartsWith("$2a$")));
    }

    [Fact]
    public async Task Login_NonexistentEmail_StillCallsBcryptForTimingSafety()
    {
        var setup = new AuthServiceTestSetup();
        setup.UserRepo.GetByEmailAsync("ghost@test.com", Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var sut = setup.Build();

        var act = () => sut.LoginAsync(new LoginDto("ghost@test.com", "any"));

        await act.Should().ThrowAsync<UnauthorizedException>();
        setup.PasswordHasher.Received(1).Verify("any", Arg.Is<string>(h => h.StartsWith("$2a$")));
    }

    [Fact]
    public async Task RefreshToken_ReplayDetected_RevokesAllUserSessions()
    {
        var setup = new AuthServiceTestSetup();
        var userId = Guid.NewGuid();
        var stolenToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = "stolen",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsRevoked = true,
            RevokedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        setup.RefreshTokenRepo.GetByTokenAsync("stolen", Arg.Any<CancellationToken>())
            .Returns(stolenToken);

        var sut = setup.Build();

        var act = () => sut.RefreshTokenAsync("stolen");

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("*reuse detected*");

        await setup.RefreshTokenRepo.Received(1)
            .RevokeAllForUserAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmEmail_ValidToken_MarksConfirmedAndClearsToken()
    {
        var setup = new AuthServiceTestSetup();
        var user = new UserBuilder()
            .WithEmail("pending@test.com")
            .Unconfirmed("HASH-XYZ", DateTime.UtcNow.AddHours(12))
            .Build();
        setup.UserRepo.GetByEmailAsync("pending@test.com", Arg.Any<CancellationToken>()).Returns(user);
        setup.TokenGenerator.Verify("plain-xyz", "HASH-XYZ").Returns(true);

        var sut = setup.Build();

        await sut.ConfirmEmailAsync(new ConfirmEmailDto("pending@test.com", "plain-xyz"));

        user.EmailConfirmed.Should().BeTrue();
        user.EmailConfirmationTokenHash.Should().BeNull();
        user.EmailConfirmationTokenExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task ConfirmEmail_ExpiredToken_ThrowsGenericError()
    {
        var setup = new AuthServiceTestSetup();
        var user = new UserBuilder()
            .WithExpiredConfirmationToken("HASH-OLD")
            .Build();
        setup.UserRepo.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

        var sut = setup.Build();

        var act = () => sut.ConfirmEmailAsync(new ConfirmEmailDto("test@example.com", "any"));

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("*Invalid or expired*");

        // Verify не должен даже вызываться — сразу отказ по дате
        setup.TokenGenerator.DidNotReceive().Verify(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task RequestPasswordReset_NonexistentEmail_SilentSuccess()
    {
        var setup = new AuthServiceTestSetup();
        setup.UserRepo.GetByEmailAsync("ghost@test.com", Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var sut = setup.Build();

        await sut.RequestPasswordResetAsync(new RequestPasswordResetDto("ghost@test.com"));

        await setup.OutboxRepo.DidNotReceive()
            .AddAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetPassword_Success_RevokesAllRefreshTokens()
    {
        var setup = new AuthServiceTestSetup();
        var userId = Guid.NewGuid();
        var user = new UserBuilder()
            .WithId(userId)
            .WithEmail("reset@test.com")
            .WithPasswordResetToken("HASH-RESET", DateTime.UtcNow.AddMinutes(30))
            .Build();
        setup.UserRepo.GetByEmailAsync("reset@test.com", Arg.Any<CancellationToken>()).Returns(user);
        setup.TokenGenerator.Verify("plain-reset", "HASH-RESET").Returns(true);
        setup.PasswordHasher.Hash("NewPassword1").Returns("new-hashed");

        var sut = setup.Build();

        await sut.ResetPasswordAsync(
            new ResetPasswordDto("reset@test.com", "plain-reset", "NewPassword1"));

        user.PasswordHash.Should().Be("new-hashed");
        user.PasswordResetTokenHash.Should().BeNull();

        await setup.RefreshTokenRepo.Received(1)
            .RevokeAllForUserAsync(userId, Arg.Any<CancellationToken>());
    }
}