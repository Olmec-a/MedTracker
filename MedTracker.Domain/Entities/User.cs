using MedTracker.Domain.Enums;

namespace MedTracker.Domain.Entities;

public class User : AuditableEntity
{
    public string Login { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int Age { get; set; }
    public UserRole Role { get; set; } = UserRole.User;

    // Navigation properties
    public ICollection<UserDiagnosis> UserDiagnoses { get; set; } = new List<UserDiagnosis>();
    public ICollection<UserMedication> UserMedications { get; set; } = new List<UserMedication>();
    public ICollection<UserSupplement> UserSupplements { get; set; } = new List<UserSupplement>();
    public ICollection<UserSideEffectLog> SideEffectLogs { get; set; } = new List<UserSideEffectLog>();
    public ICollection<ExternalMedication> ExternalMedications { get; set; } = new List<ExternalMedication>();
    public ICollection<MenstrualCycleEntry> MenstrualCycleEntries { get; set; } = new List<MenstrualCycleEntry>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}