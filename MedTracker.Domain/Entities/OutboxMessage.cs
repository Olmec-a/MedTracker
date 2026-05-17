namespace MedTracker.Domain.Entities;

/// <summary>
/// Outbox-сообщение для гарантированной доставки писем.
/// Записывается в одной транзакции с user-операцией (регистрация, password reset),
/// а фоновый OutboxProcessor забирает и шлёт через IEmailSender.
/// Если SendGrid временно недоступен — сообщение остаётся в БД и будет повторно обработано.
/// </summary>
public class OutboxMessage : BaseEntity
{
    public string MessageType { get; set; } = "Email"; // На будущее: SMS, Push и т.д.

    // Email payload
    public string ToAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string? BodyPlainText { get; set; }

    // Lifecycle
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public string? LastError { get; set; }

    /// <summary>Optimistic concurrency token, чтобы две реплики не схватили одно сообщение.</summary>
    public Guid LockToken { get; set; } = Guid.NewGuid();
    public DateTime? LockedUntil { get; set; }
    /// <summary>
    /// CorrelationId исходного запроса, породившего это сообщение.
    /// Используется для трассировки цепочки "API → outbox → Hangfire → SendGrid"
    /// в логах через {CorrelationId} property.
    /// Может быть null для сообщений из системных задач (cleanup и т.п.).
    /// </summary>
    public string? CorrelationId { get; set; }
}