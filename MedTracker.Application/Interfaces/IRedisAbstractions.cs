namespace MedTracker.Application.Interfaces;

/// <summary>
/// Атомарный счётчик версии каталога.
/// Реализация — Redis (INCR / GET) в Infrastructure.
/// </summary>
public interface ICatalogVersionStore
{
    /// <summary>Текущая версия (если ключа нет — вернёт 1 и инициализирует).</summary>
    Task<long> GetCurrentAsync(CancellationToken ct = default);

    /// <summary>Атомарно инкрементирует версию. Возвращает новое значение.</summary>
    Task<long> BumpAsync(CancellationToken ct = default);
}

/// <summary>
/// Distributed rate limiter поверх Redis (fixed window).
/// Используется RateLimitInterceptor'ом в gRPC слое.
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Разрешён ли запрос для пары (key, окно).
    /// Внутри атомарно делает INCR + EXPIRE и проверяет, не превышен ли лимит.
    /// </summary>
    Task<RateLimitResult> CheckAsync(string key, int limit, TimeSpan window, CancellationToken ct = default);
}

public record RateLimitResult(bool Allowed, int CurrentCount, int Limit, TimeSpan ResetAfter);

/// <summary>
/// Distributed snapshot статуса пользователя для AuthInterceptor'а
/// (exists + LockoutUntil). Кешируется через HybridCache (L1+L2).
/// </summary>
public interface IUserStatusCache
{
    Task<UserStatusSnapshot?> GetAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Принудительная инвалидация — например, после смены пароля или принудительного logout.
    /// </summary>
    Task InvalidateAsync(Guid userId, CancellationToken ct = default);
}

public record UserStatusSnapshot(bool Exists, DateTime? LockoutUntil);