using MedTracker.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace MedTracker.Infrastructure.Services;

public class AppUrlOptions
{
    /// <summary>Публичный базовый URL фронтенда / API gateway, например https://medtracker.app</summary>
    public string BaseUrl { get; set; } = "http://localhost:3000";

    public string EmailConfirmationPath { get; set; } = "/auth/confirm-email";
    public string PasswordResetPath { get; set; } = "/auth/reset-password";
}

public class AppUrlBuilder : IAppUrlBuilder
{
    private readonly AppUrlOptions _options;

    public AppUrlBuilder(IOptions<AppUrlOptions> options)
    {
        _options = options.Value;
    }

    public string BuildEmailConfirmationUrl(string email, string token)
        => Build(_options.EmailConfirmationPath, email, token);

    public string BuildPasswordResetUrl(string email, string token)
        => Build(_options.PasswordResetPath, email, token);

    private string Build(string path, string email, string token)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var p = path.StartsWith('/') ? path : "/" + path;
        var encodedEmail = Uri.EscapeDataString(email);
        var encodedToken = Uri.EscapeDataString(token);
        return $"{baseUrl}{p}?email={encodedEmail}&token={encodedToken}";
    }
}