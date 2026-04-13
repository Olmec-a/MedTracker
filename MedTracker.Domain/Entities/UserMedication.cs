namespace MedTracker.Domain.Entities;

public class UserMedication : SoftDeletableEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid MedicationId { get; set; }
    public Medication Medication { get; set; } = null!;

    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
}