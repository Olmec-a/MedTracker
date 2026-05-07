using MedTracker.Domain.Enums;

namespace MedTracker.Domain.Entities;

public class User : AuditableEntity
{
    // Email теперь основной идентификатор для логина (заменяет старый Login).
    // В БД колонка осталась "Login" во избежание ломающей миграции данных,
    // но логически и в коде это email.
    // → См. миграцию: rename column Login → Email, расширение длины до 254.
    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int Age { get; set; }
    public UserRole Role { get; set; } = UserRole.User;

    // ── Email confirmation ──
    public bool EmailConfirmed { get; set; }
    /// <summary>SHA-256 hash от plaintext-токена. Сам токен в БД не хранится.</summary>
    public string? EmailConfirmationTokenHash { get; set; }
    public DateTime? EmailConfirmationTokenExpiresAt { get; set; }

    // ── Password reset ──
    public string? PasswordResetTokenHash { get; set; }
    public DateTime? PasswordResetTokenExpiresAt { get; set; }

    // ── Lockout protection ──
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutUntil { get; set; }

    // Navigation properties
    public ICollection<UserDiagnosis> UserDiagnoses { get; set; } = new List<UserDiagnosis>();
    public ICollection<UserMedication> UserMedications { get; set; } = new List<UserMedication>();
    public ICollection<UserSupplement> UserSupplements { get; set; } = new List<UserSupplement>();
    public ICollection<UserSideEffectLog> SideEffectLogs { get; set; } = new List<UserSideEffectLog>();
    public ICollection<ExternalMedication> ExternalMedications { get; set; } = new List<ExternalMedication>();
    public ICollection<MenstrualCycleEntry> MenstrualCycleEntries { get; set; } = new List<MenstrualCycleEntry>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}