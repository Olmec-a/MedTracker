namespace MedTracker.Domain.Entities;

public class SideEffect : BaseEntity
{
    public Guid MedicationId { get; set; }
    public Medication Medication { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public ICollection<UserSideEffectLog> UserSideEffectLogs { get; set; } = new List<UserSideEffectLog>();
}