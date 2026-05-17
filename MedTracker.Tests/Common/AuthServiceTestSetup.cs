using FluentValidation;
using MedTracker.Application.DTOs;
using MedTracker.Application.Interfaces;
using MedTracker.Application.Services;
using MedTracker.Application.Validators;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MedTracker.Tests.Common;

/// <summary>
/// Builder тестового AuthService с моками всех зависимостей.
/// Возвращает сам сервис + публично доступные моки, чтобы в тесте можно было
/// настраивать поведение и asserts.
///
/// Обновление: добавлен ICorrelationIdAccessor (17-й параметр AuthService).
/// Возвращает фиксированный "test-correlation-id" — никакой тест на это не
/// рассчитывает напрямую, но без мока конструктор упадёт с NullReferenceException.
/// </summary>
public class AuthServiceTestSetup
{
    public IUserRepository UserRepo { get; } = Substitute.For<IUserRepository>();
    public IRefreshTokenRepository RefreshTokenRepo { get; } = Substitute.For<IRefreshTokenRepository>();
    public IOutboxRepository OutboxRepo { get; } = Substitute.For<IOutboxRepository>();
    public IJwtService JwtService { get; } = Substitute.For<IJwtService>();
    public IPasswordHasher PasswordHasher { get; } = Substitute.For<IPasswordHasher>();
    public ITokenGenerator TokenGenerator { get; } = Substitute.For<ITokenGenerator>();
    public IEmailTemplateService EmailTemplates { get; } = Substitute.For<IEmailTemplateService>();
    public IAppUrlBuilder UrlBuilder { get; } = Substitute.For<IAppUrlBuilder>();
    public ICorrelationIdAccessor CorrelationIdAccessor { get; } = Substitute.For<ICorrelationIdAccessor>(); // ← НОВЫЙ

    public AuthServiceTestSetup()
    {
        // Дефолты, чтобы тесты не падали из-за NRE на тривиальных вызовах
        JwtService.GenerateAccessToken(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns("test-access-token");
        JwtService.GenerateRefreshToken().Returns(_ => Guid.NewGuid().ToString("N"));
        JwtService.GetExpiresAtUnix().Returns(DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds());

        TokenGenerator.GenerateToken().Returns(("plain-token-xxx", "HASH-OF-PLAIN-TOKEN"));

        EmailTemplates.RenderConfirmation(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new EmailTemplate("Confirm", "<html/>", "plain"));
        EmailTemplates.RenderPasswordReset(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new EmailTemplate("Reset", "<html/>", "plain"));

        UrlBuilder.BuildEmailConfirmationUrl(Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => $"https://x.test/confirm?email={ci.ArgAt<string>(0)}&token={ci.ArgAt<string>(1)}");
        UrlBuilder.BuildPasswordResetUrl(Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => $"https://x.test/reset?email={ci.ArgAt<string>(0)}&token={ci.ArgAt<string>(1)}");

        // CorrelationId — фиксированный для всех тестов. Если когда-нибудь захочется
        // проверить, что он попадает в OutboxMessage — можно искать "test-correlation-id".
        CorrelationIdAccessor.Current.Returns("test-correlation-id");
    }

    public AuthService Build()
    {
        return new AuthService(
            UserRepo, RefreshTokenRepo, OutboxRepo,
            JwtService, PasswordHasher, TokenGenerator,
            EmailTemplates, UrlBuilder,
            new RegisterDtoValidator(),
            new LoginDtoValidator(),
            new ChangePasswordDtoValidator(),
            new ConfirmEmailDtoValidator(),
            new ResendConfirmationDtoValidator(),
            new RequestPasswordResetDtoValidator(),
            new ResetPasswordDtoValidator(),
            NullLogger<AuthService>.Instance,
            CorrelationIdAccessor); // ← НОВЫЙ, последний параметр
    }
}