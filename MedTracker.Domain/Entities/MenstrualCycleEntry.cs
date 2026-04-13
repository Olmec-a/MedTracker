using MedTracker.Domain.Enums;

namespace MedTracker.Domain.Entities;

public class MenstrualCycleEntry : SoftDeletableEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public CycleIntensity Intensity { get; set; }
    public List<string> Symptoms { get; set; } = new();
    public string? Notes { get; set; }
}