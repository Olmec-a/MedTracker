namespace MedTracker.Application.Interfaces;

/// <summary>
/// Сервисы периодической очистки. Вызываются Hangfire'ом по расписанию.
///
/// Все методы возвращают количество удалённых/обнулённых записей —
/// это попадает в Hangfire job result и видно в логах для observability.
/// </summary>
public interface ICleanupService
{
    /// <summary>Удаляет refresh-токены, у которых ExpiresAt &lt; now.</summary>
    Task<int> CleanupExpiredRefreshTokensAsync(CancellationToken ct = default);

    /// <summary>
    /// Обнуляет EmailConfirmationTokenHash/EmailConfirmationTokenExpiresAt у юзеров,
    /// у которых ExpiresAt уже прошёл (но юзер так и не подтвердил email).
    /// </summary>
    Task<int> CleanupExpiredEmailConfirmationTokensAsync(CancellationToken ct = default);

    /// <summary>То же для password-reset токенов.</summary>
    Task<int> CleanupExpiredPasswordResetTokensAsync(CancellationToken ct = default);
}