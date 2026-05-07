using MedTracker.Application.DTOs;

namespace MedTracker.Application.Interfaces;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public interface IAuthService
{
    Task<AuthResultDto> RegisterAsync(RegisterDto dto, CancellationToken ct = default);
    Task<AuthResultDto> LoginAsync(LoginDto dto, CancellationToken ct = default);
    Task<AuthResultDto> RefreshTokenAsync(string refreshToken, CancellationToken ct = default);
    Task ChangePasswordAsync(Guid userId, ChangePasswordDto dto, CancellationToken ct = default);
    Task LogoutAsync(Guid userId, CancellationToken ct = default);

    // ── New: email confirmation & password reset ──
    Task ConfirmEmailAsync(ConfirmEmailDto dto, CancellationToken ct = default);
    Task ResendConfirmationAsync(ResendConfirmationDto dto, CancellationToken ct = default);
    Task RequestPasswordResetAsync(RequestPasswordResetDto dto, CancellationToken ct = default);
    Task ResetPasswordAsync(ResetPasswordDto dto, CancellationToken ct = default);
}

public interface IJwtService
{
    string GenerateAccessToken(Guid userId, string email, string role);
    string GenerateRefreshToken();
    long GetExpiresAtUnix();
}

/// <summary>
/// Генерирует high-entropy токены и хеширует их для безопасного хранения.
/// Используется для email confirmation и password reset.
/// </summary>
public interface ITokenGenerator
{
    /// <returns>Plaintext-токен (отправляется в письме) и его хеш (хранится в БД).</returns>
    (string Plaintext, string Hash) GenerateToken();

    /// <summary>Constant-time сравнение plaintext-токена с хранимым хешем.</summary>
    bool Verify(string plaintext, string storedHash);
}

/// <summary>Низкоуровневая отправка письма (SendGrid в продакшне).</summary>
public interface IEmailSender
{
    Task SendAsync(string toAddress, string subject, string htmlBody, string? plainBody = null, CancellationToken ct = default);
}

/// <summary>Рендерит HTML-шаблоны писем (на старте — простые строки в коде).</summary>
public interface IEmailTemplateService
{
    EmailTemplate RenderConfirmation(string fullName, string confirmationUrl);
    EmailTemplate RenderPasswordReset(string fullName, string resetUrl);
}

public record EmailTemplate(string Subject, string HtmlBody, string PlainBody);

public interface IUserProfileService
{
    Task<UserProfileDto> GetProfileAsync(Guid userId, CancellationToken ct = default);
    Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateProfileDto dto, CancellationToken ct = default);
    Task<List<UserDiagnosisDto>> AssignDiagnosesAsync(Guid userId, List<Guid> diagnosisIds, CancellationToken ct = default);
    Task<List<UserDiagnosisDto>> GetUserDiagnosesAsync(Guid userId, CancellationToken ct = default);
}

public interface IMedicationCatalogService
{
    Task<List<DiagnosisDto>> GetDiagnosesAsync(CancellationToken ct = default);
    Task<PaginatedResultDto<MedicationDto>> GetMedicationsByDiagnosisAsync(Guid diagnosisId, int page, int pageSize, CancellationToken ct = default);
    Task<PaginatedResultDto<SupplementDto>> GetSupplementsByMedicationAsync(Guid medicationId, int page, int pageSize, CancellationToken ct = default);
    Task<PaginatedResultDto<SideEffectDto>> GetSideEffectsByMedicationAsync(Guid medicationId, int page, int pageSize, CancellationToken ct = default);
}

public interface IUserMedicationService
{
    Task<UserMedicationDto> AssignMedicationAsync(Guid userId, AssignMedicationDto dto, CancellationToken ct = default);
    Task RemoveMedicationAsync(Guid userId, Guid userMedicationId, CancellationToken ct = default);
    Task<List<UserMedicationDto>> GetUserMedicationsAsync(Guid userId, CancellationToken ct = default);
    Task<UserSupplementDto> AssignSupplementAsync(Guid userId, AssignSupplementDto dto, CancellationToken ct = default);
    Task RemoveSupplementAsync(Guid userId, Guid userSupplementId, CancellationToken ct = default);
    Task<List<UserSupplementDto>> GetUserSupplementsAsync(Guid userId, CancellationToken ct = default);
}

public interface ISideEffectLogService
{
    Task<SideEffectLogDto> LogSideEffectAsync(Guid userId, CreateSideEffectLogDto dto, CancellationToken ct = default);
    Task<PaginatedResultDto<SideEffectLogDto>> GetLogsAsync(Guid userId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default);
    Task DeleteLogAsync(Guid userId, Guid logId, CancellationToken ct = default);
}

public interface IExternalMedicationService
{
    Task<ExternalMedicationDto> AddAsync(Guid userId, CreateExternalMedicationDto dto, CancellationToken ct = default);
    Task<PaginatedResultDto<ExternalMedicationDto>> GetAsync(Guid userId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);
}

public interface IMenstrualCycleService
{
    Task<MenstrualCycleDto> AddEntryAsync(Guid userId, CreateCycleEntryDto dto, CancellationToken ct = default);
    Task<MenstrualCycleDto> UpdateEntryAsync(Guid userId, UpdateCycleEntryDto dto, CancellationToken ct = default);
    Task<PaginatedResultDto<MenstrualCycleDto>> GetHistoryAsync(Guid userId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default);
    Task DeleteEntryAsync(Guid userId, Guid entryId, CancellationToken ct = default);
}

public interface IExcelImportService
{
    Task<ImportResultDto> ImportAsync(byte[] fileBytes, string fileName, string diagnosisName, Guid importedByUserId, CancellationToken ct = default);
    Task<List<ImportRecordDto>> GetImportHistoryAsync(CancellationToken ct = default);
}