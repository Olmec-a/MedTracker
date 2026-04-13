namespace MedTracker.Domain.Entities;

public class UserDiagnosis
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid DiagnosisId { get; set; }
    public Diagnosis Diagnosis { get; set; } = null!;

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}