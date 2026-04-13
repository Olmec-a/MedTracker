namespace MedTracker.Domain.Entities;

public class Medication : BaseEntity
{
    public Guid DiagnosisId { get; set; }
    public Diagnosis Diagnosis { get; set; } = null!;

    public string HormonalGroup { get; set; } = string.Empty;
    public string INN { get; set; } = string.Empty;
    public string TradeName { get; set; } = string.Empty;
    public string Dosage { get; set; } = string.Empty;
    public string Form { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public string Diet { get; set; } = string.Empty;

    public ICollection<Supplement> Supplements { get; set; } = new List<Supplement>();
    public ICollection<SideEffect> SideEffects { get; set; } = new List<SideEffect>();
    public ICollection<UserMedication> UserMedications { get; set; } = new List<UserMedication>();
}