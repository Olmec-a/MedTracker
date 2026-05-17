using MedTracker.Domain.Entities;
using MedTracker.Domain.Enums;

namespace MedTracker.Tests.Common.Builders;

/// <summary>
/// Билдер тестового User. По умолчанию — нормальный confirmed user без lockout.
/// Каждый With* возвращает this — fluent API.
/// </summary>
public class UserBuilder
{
    private Guid _id = Guid.NewGuid();
    private string _email = "test@example.com";
    private string _passwordHash = "$2a$11$dummyhashbutprobablyfine................";
    private string _fullName = "Test User";
    private int _age = 30;
    private UserRole _role = UserRole.User;
    private bool _emailConfirmed = true;
    private string? _emailConfirmationTokenHash;
    private DateTime? _emailConfirmationTokenExpiresAt;
    private string? _passwordResetTokenHash;
    private DateTime? _passwordResetTokenExpiresAt;
    private int _failedLoginAttempts;
    private DateTime? _lockoutUntil;

    public UserBuilder WithId(Guid id) { _id = id; return this; }
    public UserBuilder WithEmail(string email) { _email = email; return this; }
    public UserBuilder WithPasswordHash(string hash) { _passwordHash = hash; return this; }
    public UserBuilder WithRole(UserRole role) { _role = role; return this; }

    public UserBuilder Unconfirmed(string tokenHash, DateTime expiresAt)
    {
        _emailConfirmed = false;
        _emailConfirmationTokenHash = tokenHash;
        _emailConfirmationTokenExpiresAt = expiresAt;
        return this;
    }

    public UserBuilder WithExpiredConfirmationToken(string tokenHash)
    {
        _emailConfirmed = false;
        _emailConfirmationTokenHash = tokenHash;
        _emailConfirmationTokenExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        return this;
    }

    public UserBuilder WithPasswordResetToken(string tokenHash, DateTime expiresAt)
    {
        _passwordResetTokenHash = tokenHash;
        _passwordResetTokenExpiresAt = expiresAt;
        return this;
    }

    public UserBuilder WithFailedAttempts(int attempts) { _failedLoginAttempts = attempts; return this; }
    public UserBuilder LockedUntil(DateTime until) { _lockoutUntil = until; return this; }

    public User Build() => new()
    {
        Id = _id,
        Email = _email,
        PasswordHash = _passwordHash,
        FullName = _fullName,
        Age = _age,
        Role = _role,
        EmailConfirmed = _emailConfirmed,
        EmailConfirmationTokenHash = _emailConfirmationTokenHash,
        EmailConfirmationTokenExpiresAt = _emailConfirmationTokenExpiresAt,
        PasswordResetTokenHash = _passwordResetTokenHash,
        PasswordResetTokenExpiresAt = _passwordResetTokenExpiresAt,
        FailedLoginAttempts = _failedLoginAttempts,
        LockoutUntil = _lockoutUntil,
        CreatedAt = DateTime.UtcNow.AddDays(-7),
        UpdatedAt = DateTime.UtcNow.AddDays(-1)
    };
}