namespace MedTracker.Domain.Entities;

public class UserSupplement : SoftDeletableEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid SupplementId { get; set; }
    public Supplement Supplement { get; set; } = null!;

    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
}