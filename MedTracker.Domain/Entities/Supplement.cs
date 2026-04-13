namespace MedTracker.Domain.Entities;

public class Supplement : BaseEntity
{
    public Guid MedicationId { get; set; }
    public Medication Medication { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string Dosage { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;

    public ICollection<UserSupplement> UserSupplements { get; set; } = new List<UserSupplement>();
}