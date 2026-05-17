namespace MedTracker.Infrastructure.Services;

/// <summary>
/// Конфигурация SMTP-провайдера. Привязывается из секции "Smtp" в appsettings.
///
/// Для Mailtrap (dev sandbox):
///   Host = "sandbox.smtp.mailtrap.io"
///   Port = 2525 (или 587, 465, 25 — Mailtrap слушает все)
///   UseStartTls = true
///   Username = из Mailtrap UI (отдельный для каждого inbox'а)
///   Password = из Mailtrap UI
///
/// Для prod (SendGrid через SMTP-relay, Yandex, Gmail и т.д.) — те же поля,
/// только хост/порт/credentials другие.
/// </summary>
public class SmtpOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;

    /// <summary>STARTTLS (порты 587, 2525). Для implicit TLS на 465 — false.</summary>
    public bool UseStartTls { get; set; } = true;

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>From-адрес. Должен быть верифицирован у провайдера для prod.</summary>
    public string FromAddress { get; set; } = "noreply@medtracker.local";
    public string FromName { get; set; } = "MedTracker";

    /// <summary>Таймаут на установку соединения и отправку, в секундах.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}