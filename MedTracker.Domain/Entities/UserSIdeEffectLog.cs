using MedTracker.Domain.Enums;

namespace MedTracker.Domain.Entities;

public class UserSideEffectLog : SoftDeletableEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid SideEffectId { get; set; }
    public SideEffect SideEffect { get; set; } = null!;

    public DateTime Date { get; set; }
    public SideEffectIntensity Intensity { get; set; }
    public string? Comment { get; set; }
}