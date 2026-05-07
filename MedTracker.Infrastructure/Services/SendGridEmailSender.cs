using MedTracker.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace MedTracker.Infrastructure.Services;

public class SendGridOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "MedTracker";
}

public class SendGridEmailSender : IEmailSender
{
    private readonly ISendGridClient _client;
    private readonly SendGridOptions _options;
    private readonly ILogger<SendGridEmailSender> _logger;

    public SendGridEmailSender(
        ISendGridClient client,
        IOptions<SendGridOptions> options,
        ILogger<SendGridEmailSender> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(
        string toAddress, string subject, string htmlBody, string? plainBody = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("SendGrid:ApiKey is not configured.");
        if (string.IsNullOrWhiteSpace(_options.FromEmail))
            throw new InvalidOperationException("SendGrid:FromEmail is not configured.");

        var from = new EmailAddress(_options.FromEmail, _options.FromName);
        var to = new EmailAddress(toAddress);
        var msg = MailHelper.CreateSingleEmail(from, to, subject, plainBody ?? StripHtml(htmlBody), htmlBody);

        var response = await _client.SendEmailAsync(msg, ct);

        if ((int)response.StatusCode >= 400)
        {
            var body = await response.Body.ReadAsStringAsync(ct);
            _logger.LogError("SendGrid returned {StatusCode} for {ToAddress}: {Body}",
                response.StatusCode, toAddress, body);
            throw new InvalidOperationException(
                $"SendGrid returned {(int)response.StatusCode}: {body}");
        }

        _logger.LogInformation("Email sent to {ToAddress} (subject: {Subject})", toAddress, subject);
    }

    private static string StripHtml(string html)
    {
        // Минимальный fallback на случай, если plain-text не передан явно.
        // В продакшне передавайте plain отдельно — это лучше для deliverability и accessibility.
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ").Trim();
    }
}