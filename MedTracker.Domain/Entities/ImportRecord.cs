namespace MedTracker.Domain.Entities;

public class ImportRecord : BaseEntity
{
    public string FileName { get; set; } = string.Empty;
    public string DiagnosisName { get; set; } = string.Empty;
    public int RecordsImported { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public Guid ImportedByUserId { get; set; }
    public User ImportedBy { get; set; } = null!;
}