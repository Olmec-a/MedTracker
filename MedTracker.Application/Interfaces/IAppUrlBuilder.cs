namespace MedTracker.Application.Interfaces;

/// <summary>
/// Строит публичные URL для писем (подтверждение email, password reset).
/// База — App:BaseUrl из конфигурации. Реализация — в Infrastructure.
/// </summary>
public interface IAppUrlBuilder
{
    string BuildEmailConfirmationUrl(string email, string token);
    string BuildPasswordResetUrl(string email, string token);
}