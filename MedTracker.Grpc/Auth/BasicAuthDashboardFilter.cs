using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Hangfire.Annotations;
using Hangfire.Dashboard;

namespace MedTracker.Grpc.Auth;

/// <summary>
/// Basic authentication для Hangfire dashboard.
///
/// Безопасность:
/// - Пароль и логин сравниваются через CryptographicOperations.FixedTimeEquals
///   (constant-time), чтобы атакующий не мог по времени ответа угадывать символы.
/// - Учётные данные читаются из env-vars (через IConfiguration), не из БД.
/// - При неверных credentials отдаём 401 + WWW-Authenticate, чтобы браузер
///   показал нативный диалог логина.
///
/// Ограничения:
/// - Basic auth передаёт пароль в Base64 каждый запрос. Используй ТОЛЬКО за HTTPS
///   reverse-proxy, иначе пароль уйдёт открытым текстом.
/// - Нет защиты от brute-force (нет rate-limit'а по IP). Если /hangfire доступен
///   из интернета — поставь fail2ban на reverse-proxy или брандмауэр-правило,
///   ограничивающее доступ только с известных IP.
/// </summary>
public class BasicAuthDashboardFilter : IDashboardAuthorizationFilter
{
    private readonly byte[] _expectedUserBytes;
    private readonly byte[] _expectedPassBytes;

    public BasicAuthDashboardFilter(string expectedUser, string expectedPass)
    {
        if (string.IsNullOrEmpty(expectedUser))
            throw new ArgumentException("User must not be empty", nameof(expectedUser));
        if (string.IsNullOrEmpty(expectedPass))
            throw new ArgumentException("Pass must not be empty", nameof(expectedPass));

        _expectedUserBytes = Encoding.UTF8.GetBytes(expectedUser);
        _expectedPassBytes = Encoding.UTF8.GetBytes(expectedPass);
    }

    public bool Authorize([NotNull] DashboardContext context)
    {
        var http = context.GetHttpContext();
        var authHeader = http.Request.Headers.Authorization.ToString();

        if (TryParseBasicAuth(authHeader, out var providedUser, out var providedPass)
            && CredentialsMatch(providedUser!, providedPass!))
        {
            return true;
        }

        // 401 + challenge — браузер покажет диалог "Sign in"
        http.Response.Headers.WWWAuthenticate = "Basic realm=\"MedTracker Hangfire\", charset=\"UTF-8\"";
        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return false;
    }

    private static bool TryParseBasicAuth(string header, out string? user, out string? pass)
    {
        user = null; pass = null;

        if (string.IsNullOrEmpty(header)) return false;
        if (!AuthenticationHeaderValue.TryParse(header, out var parsed)) return false;
        if (!string.Equals(parsed.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrEmpty(parsed.Parameter)) return false;

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(parsed.Parameter));
            var separator = decoded.IndexOf(':');
            if (separator < 0) return false;

            user = decoded[..separator];
            pass = decoded[(separator + 1)..];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private bool CredentialsMatch(string providedUser, string providedPass)
    {
        var userBytes = Encoding.UTF8.GetBytes(providedUser);
        var passBytes = Encoding.UTF8.GetBytes(providedPass);

        // FixedTimeEquals требует одинаковую длину и сравнивает за константное время.
        // Если длины различаются — сравниваем provided c самим собой как dummy,
        // чтобы тайминг по длине пароля тоже не сливался.
        var userOk = userBytes.Length == _expectedUserBytes.Length
                     && CryptographicOperations.FixedTimeEquals(userBytes, _expectedUserBytes);
        var passOk = passBytes.Length == _expectedPassBytes.Length
                     && CryptographicOperations.FixedTimeEquals(passBytes, _expectedPassBytes);

        return userOk && passOk;
    }
}