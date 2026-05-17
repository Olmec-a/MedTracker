using MailKit.Net.Smtp;
using MailKit.Security;
using MedTracker.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace MedTracker.Infrastructure.Services;

/// <summary>
/// SMTP-реализация <see cref="IEmailSender"/> через MailKit.
///
/// MailKit (а не System.Net.Mail) потому что:
///   1. Microsoft официально рекомендует MailKit вместо устаревшего SmtpClient.
///   2. Лучше поддержка современных стандартов (STARTTLS, OAUTH2, DKIM).
///   3. Корректное освобождение ресурсов через async-friendly API.
///
/// Новое соединение создаётся на каждое письмо. Для high-throughput сервиса
/// имеет смысл переиспользовать SmtpClient через connection pool, но для
/// outbox-паттерна (одно письмо = одна Hangfire-джоба) это не нужно.
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.Host))
            throw new InvalidOperationException(
                "SMTP host is not configured. Set Smtp:Host in appsettings " +
                "or environment variable Smtp__Host.");
    }

    public async Task SendAsync(
        string toAddress,
        string subject,
        string htmlBody,
        string? plainBody = null,
        CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = subject;

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = htmlBody,
            TextBody = plainBody ?? StripHtml(htmlBody)
        };
        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient
        {
            Timeout = _options.TimeoutSeconds * 1000
        };

        try
        {
            // Выбор режима SSL/TLS:
            //   - SecureSocketOptions.StartTls — для порта 587 (и 2525 у Mailtrap)
            //   - SecureSocketOptions.SslOnConnect — для порта 465 (implicit TLS)
            //   - Auto — MailKit решит сам, обычно достаточно надёжно
            var secureOptions = _options.UseStartTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.Auto;

            await client.ConnectAsync(_options.Host, _options.Port, secureOptions, ct);

            if (!string.IsNullOrEmpty(_options.Username))
                await client.AuthenticateAsync(_options.Username, _options.Password, ct);

            await client.SendAsync(message, ct);

            _logger.LogInformation(
                "Email sent to {Recipient} via {Host}:{Port}, subject={Subject}",
                toAddress, _options.Host, _options.Port, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send email to {Recipient} via {Host}:{Port}",
                toAddress, _options.Host, _options.Port);
            // Пробрасываем — Hangfire поставит на retry (outbox-паттерн)
            throw;
        }
        finally
        {
            if (client.IsConnected)
                await client.DisconnectAsync(true, ct);
        }
    }

    /// <summary>
    /// Грубое преобразование HTML → plain text для писем без явного plainBody.
    /// Не идеально, но достаточно для fallback'а.
    /// </summary>
    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        return System.Text.RegularExpressions.Regex
            .Replace(html, "<.*?>", string.Empty)
            .Trim();
    }
}