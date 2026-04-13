namespace MedTracker.Domain.Entities;

public class Diagnosis : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public ICollection<Medication> Medications { get; set; } = new List<Medication>();
    public ICollection<UserDiagnosis> UserDiagnoses { get; set; } = new List<UserDiagnosis>();
}