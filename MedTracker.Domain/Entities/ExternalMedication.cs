namespace MedTracker.Domain.Entities;

public class ExternalMedication : SoftDeletableEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string Dosage { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string? Comment { get; set; }
}