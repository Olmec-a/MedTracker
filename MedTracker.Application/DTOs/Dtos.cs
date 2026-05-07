using MedTracker.Domain.Enums;

namespace MedTracker.Application.DTOs;

// Auth
public record RegisterDto(string Email, string Password, string FullName, int Age);
public record LoginDto(string Email, string Password);
public record AuthResultDto(string AccessToken, string RefreshToken, long ExpiresAt);
public record ChangePasswordDto(string CurrentPassword, string NewPassword);

// Email confirmation / password reset
public record ConfirmEmailDto(string Email, string Token);
public record ResendConfirmationDto(string Email);
public record RequestPasswordResetDto(string Email);
public record ResetPasswordDto(string Email, string Token, string NewPassword);

// User Profile (Login → Email)
public record UserProfileDto(
    Guid Id, string Email, string FullName, int Age, bool EmailConfirmed,
    DateTime CreatedAt, DateTime UpdatedAt);
public record UpdateProfileDto(string FullName, int Age);

// Diagnoses
public record DiagnosisDto(Guid Id, string Name);
public record UserDiagnosisDto(Guid DiagnosisId, string DiagnosisName, DateTime AssignedAt);

// Medication catalog
public record MedicationDto(
    Guid Id, Guid DiagnosisId, string HormonalGroup, string INN,
    string TradeName, string Dosage, string Form, string Frequency, string Diet);
public record SupplementDto(Guid Id, Guid MedicationId, string Name, string Dosage, string Frequency);
public record SideEffectDto(Guid Id, Guid MedicationId, string Name);

// User medications
public record AssignMedicationDto(Guid MedicationId, DateTime StartDate, DateTime? EndDate);
public record UserMedicationDto(
    Guid Id, Guid MedicationId, string MedicationTradeName, string MedicationINN,
    DateTime StartDate, DateTime? EndDate, bool IsActive);

// User supplements
public record AssignSupplementDto(Guid SupplementId, DateTime StartDate, DateTime? EndDate);
public record UserSupplementDto(
    Guid Id, Guid SupplementId, string SupplementName,
    DateTime StartDate, DateTime? EndDate, bool IsActive);

// Side effect logs
public record CreateSideEffectLogDto(Guid SideEffectId, DateTime Date, SideEffectIntensity Intensity, string? Comment);
public record SideEffectLogDto(
    Guid Id, Guid SideEffectId, string SideEffectName,
    DateTime Date, SideEffectIntensity Intensity, string? Comment);

// External medications
public record CreateExternalMedicationDto(string Name, string Dosage, DateTime Date, string? Comment);
public record ExternalMedicationDto(Guid Id, string Name, string Dosage, DateTime Date, string? Comment);

// Menstrual cycle
public record CreateCycleEntryDto(DateTime StartDate, DateTime? EndDate, CycleIntensity Intensity, List<string> Symptoms, string? Notes);
public record UpdateCycleEntryDto(Guid Id, DateTime StartDate, DateTime? EndDate, CycleIntensity Intensity, List<string> Symptoms, string? Notes);
public record MenstrualCycleDto(
    Guid Id, DateTime StartDate, DateTime? EndDate,
    CycleIntensity Intensity, List<string> Symptoms, string? Notes);

// Import — оригинальные сигнатуры из проекта
public record ImportResultDto(
    bool Success, int MedicationsImported, int SupplementsImported, int SideEffectsImported, string Message);
public record ImportRecordDto(
    Guid Id, string FileName, string DiagnosisName, int RecordsImported,
    DateTime ImportedAt, string ImportedBy);

// Pagination
public record PaginatedResultDto<T>(List<T> Items, int TotalCount);