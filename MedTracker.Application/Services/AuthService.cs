using FluentValidation;
using MedTracker.Application.DTOs;
using MedTracker.Application.Interfaces;
using MedTracker.Domain.Entities;
using MedTracker.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace MedTracker.Application.Services;

public class AuthService : IAuthService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ConfirmationTokenLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan PasswordResetTokenLifetime = TimeSpan.FromHours(1);

    // Dummy BCrypt hash for timing attack prevention
    private const string DummyHash = "$2a$11$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy";

    private readonly IUserRepository _userRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly IOutboxRepository _outboxRepo;
    private readonly IJwtService _jwtService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenGenerator _tokenGenerator;
    private readonly IEmailTemplateService _emailTemplates;
    private readonly IAppUrlBuilder _urlBuilder;
    private readonly IValidator<RegisterDto> _registerValidator;
    private readonly IValidator<LoginDto> _loginValidator;
    private readonly IValidator<ChangePasswordDto> _changePasswordValidator;
    private readonly IValidator<ConfirmEmailDto> _confirmValidator;
    private readonly IValidator<ResendConfirmationDto> _resendValidator;
    private readonly IValidator<RequestPasswordResetDto> _requestResetValidator;
    private readonly IValidator<ResetPasswordDto> _resetValidator;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository userRepo,
        IRefreshTokenRepository refreshTokenRepo,
        IOutboxRepository outboxRepo,
        IJwtService jwtService,
        IPasswordHasher passwordHasher,
        ITokenGenerator tokenGenerator,
        IEmailTemplateService emailTemplates,
        IAppUrlBuilder urlBuilder,
        IValidator<RegisterDto> registerValidator,
        IValidator<LoginDto> loginValidator,
        IValidator<ChangePasswordDto> changePasswordValidator,
        IValidator<ConfirmEmailDto> confirmValidator,
        IValidator<ResendConfirmationDto> resendValidator,
        IValidator<RequestPasswordResetDto> requestResetValidator,
        IValidator<ResetPasswordDto> resetValidator,
        ILogger<AuthService> logger)
    {
        _userRepo = userRepo;
        _refreshTokenRepo = refreshTokenRepo;
        _outboxRepo = outboxRepo;
        _jwtService = jwtService;
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
        _emailTemplates = emailTemplates;
        _urlBuilder = urlBuilder;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
        _changePasswordValidator = changePasswordValidator;
        _confirmValidator = confirmValidator;
        _resendValidator = resendValidator;
        _requestResetValidator = requestResetValidator;
        _resetValidator = resetValidator;
        _logger = logger;
    }

    public async Task<AuthResultDto> RegisterAsync(RegisterDto dto, CancellationToken ct = default)
    {
        await ValidateAsync(_registerValidator, dto, ct);

        var email = NormalizeEmail(dto.Email);

        if (await _userRepo.ExistsByEmailAsync(email, ct))
            throw new DuplicateException($"User with email '{email}' already exists.");

        // Generate confirmation token (plaintext goes to email, hash goes to DB)
        var (plainToken, tokenHash) = _tokenGenerator.GenerateToken();

        var user = new User
        {
            Email = email,
            PasswordHash = _passwordHasher.Hash(dto.Password),
            FullName = dto.FullName,
            Age = dto.Age,
            EmailConfirmed = false,
            EmailConfirmationTokenHash = tokenHash,
            EmailConfirmationTokenExpiresAt = DateTime.UtcNow.Add(ConfirmationTokenLifetime)
        };

        await _userRepo.AddAsync(user, ct);

        // Записываем письмо в outbox в той же транзакции, что и user.
        // Если SendGrid временно недоступен — письмо отправится позже background-воркером.
        await EnqueueConfirmationEmailAsync(user, plainToken, ct);

        await _userRepo.SaveChangesAsync(ct);

        _logger.LogInformation("User registered: {UserId}, confirmation pending", user.Id);

        return await GenerateTokensAsync(user, ct);
    }

    public async Task<AuthResultDto> LoginAsync(LoginDto dto, CancellationToken ct = default)
    {
        await ValidateAsync(_loginValidator, dto, ct);

        var email = NormalizeEmail(dto.Email);
        var user = await _userRepo.GetByEmailAsync(email, ct);

        // Timing attack prevention: всегда выполняем BCrypt
        if (user == null)
        {
            _ = _passwordHasher.Verify(dto.Password, DummyHash);
            throw new UnauthorizedException("Invalid email or password.");
        }

        if (user.LockoutUntil.HasValue && user.LockoutUntil.Value > DateTime.UtcNow)
        {
            _ = _passwordHasher.Verify(dto.Password, DummyHash);
            var minutesLeft = (int)Math.Ceiling((user.LockoutUntil.Value - DateTime.UtcNow).TotalMinutes);
            throw new UnauthorizedException($"Account is locked. Try again in {minutesLeft} minute(s).");
        }

        if (!_passwordHasher.Verify(dto.Password, user.PasswordHash))
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.LockoutUntil = DateTime.UtcNow.Add(LockoutDuration);
                user.FailedLoginAttempts = 0;
                _userRepo.Update(user);
                await _userRepo.SaveChangesAsync(ct);
                throw new UnauthorizedException(
                    $"Too many failed attempts. Account locked for {LockoutDuration.TotalMinutes} minutes.");
            }

            _userRepo.Update(user);
            await _userRepo.SaveChangesAsync(ct);
            throw new UnauthorizedException("Invalid email or password.");
        }

        // Email подтверждение проверяется ПОСЛЕ пароля, чтобы не давать enumeration:
        // атакующий не должен по разным сообщениям отличить "неверный пароль" от "не подтверждён".
        if (!user.EmailConfirmed)
        {
            // Сбросим счётчик попыток (пароль-то верный)
            user.FailedLoginAttempts = 0;
            _userRepo.Update(user);
            await _userRepo.SaveChangesAsync(ct);
            throw new UnauthorizedException(
                "Email is not confirmed. Please check your inbox or request a new confirmation link.");
        }

        user.FailedLoginAttempts = 0;
        user.LockoutUntil = null;
        _userRepo.Update(user);
        await _userRepo.SaveChangesAsync(ct);

        return await GenerateTokensAsync(user, ct);
    }

    public async Task<AuthResultDto> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var storedToken = await _refreshTokenRepo.GetByTokenAsync(refreshToken, ct)
            ?? throw new UnauthorizedException("Invalid refresh token.");

        if (storedToken.IsRevoked)
        {
            _logger.LogWarning("Refresh token replay detected for user {UserId}", storedToken.UserId);
            await _refreshTokenRepo.RevokeAllForUserAsync(storedToken.UserId, ct);
            await _refreshTokenRepo.SaveChangesAsync(ct);
            throw new UnauthorizedException("Refresh token reuse detected. All sessions revoked.");
        }

        if (storedToken.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedException("Refresh token expired.");

        var user = await _userRepo.GetByIdAsync(storedToken.UserId, ct)
            ?? throw new UnauthorizedException("User not found.");

        // Rotate
        storedToken.IsRevoked = true;
        storedToken.RevokedAt = DateTime.UtcNow;
        _refreshTokenRepo.Update(storedToken);

        var newTokens = await GenerateTokensAsync(user, ct);

        var newStoredToken = await _refreshTokenRepo.GetByTokenAsync(newTokens.RefreshToken, ct);
        if (newStoredToken != null)
        {
            storedToken.ReplacedByTokenId = newStoredToken.Id;
            _refreshTokenRepo.Update(storedToken);
            await _refreshTokenRepo.SaveChangesAsync(ct);
        }

        return newTokens;
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordDto dto, CancellationToken ct = default)
    {
        await ValidateAsync(_changePasswordValidator, dto, ct);

        var user = await _userRepo.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException(nameof(User), userId);

        if (!_passwordHasher.Verify(dto.CurrentPassword, user.PasswordHash))
            throw new UnauthorizedException("Current password is incorrect.");

        user.PasswordHash = _passwordHasher.Hash(dto.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        _userRepo.Update(user);

        await _refreshTokenRepo.RevokeAllForUserAsync(userId, ct);
        await _userRepo.SaveChangesAsync(ct);
    }

    public async Task LogoutAsync(Guid userId, CancellationToken ct = default)
    {
        await _refreshTokenRepo.RevokeAllForUserAsync(userId, ct);
        await _refreshTokenRepo.SaveChangesAsync(ct);
    }

    // ───────────────────────── Email confirmation ─────────────────────────

    public async Task ConfirmEmailAsync(ConfirmEmailDto dto, CancellationToken ct = default)
    {
        await ValidateAsync(_confirmValidator, dto, ct);

        var email = NormalizeEmail(dto.Email);
        var user = await _userRepo.GetByEmailAsync(email, ct);

        // Generic error для предотвращения user enumeration
        const string genericError = "Invalid or expired confirmation token.";

        if (user == null || user.EmailConfirmationTokenHash == null
            || user.EmailConfirmationTokenExpiresAt == null
            || user.EmailConfirmationTokenExpiresAt.Value < DateTime.UtcNow)
        {
            throw new UnauthorizedException(genericError);
        }

        if (!_tokenGenerator.Verify(dto.Token, user.EmailConfirmationTokenHash))
            throw new UnauthorizedException(genericError);

        user.EmailConfirmed = true;
        user.EmailConfirmationTokenHash = null;
        user.EmailConfirmationTokenExpiresAt = null;
        user.UpdatedAt = DateTime.UtcNow;
        _userRepo.Update(user);
        await _userRepo.SaveChangesAsync(ct);

        _logger.LogInformation("Email confirmed for user {UserId}", user.Id);
    }

    public async Task ResendConfirmationAsync(ResendConfirmationDto dto, CancellationToken ct = default)
    {
        await ValidateAsync(_resendValidator, dto, ct);

        var email = NormalizeEmail(dto.Email);
        var user = await _userRepo.GetByEmailAsync(email, ct);

        // Не выдаём существование/несуществование пользователя.
        // Если пользователь не найден или уже подтверждён — молча выходим.
        if (user == null || user.EmailConfirmed)
            return;

        var (plainToken, tokenHash) = _tokenGenerator.GenerateToken();
        user.EmailConfirmationTokenHash = tokenHash;
        user.EmailConfirmationTokenExpiresAt = DateTime.UtcNow.Add(ConfirmationTokenLifetime);
        user.UpdatedAt = DateTime.UtcNow;
        _userRepo.Update(user);

        await EnqueueConfirmationEmailAsync(user, plainToken, ct);
        await _userRepo.SaveChangesAsync(ct);
    }

    // ───────────────────────── Password reset ─────────────────────────

    public async Task RequestPasswordResetAsync(RequestPasswordResetDto dto, CancellationToken ct = default)
    {
        await ValidateAsync(_requestResetValidator, dto, ct);

        var email = NormalizeEmail(dto.Email);
        var user = await _userRepo.GetByEmailAsync(email, ct);

        // Тихий выход для предотвращения user enumeration —
        // ответ всегда успешный, независимо от того, существует email или нет.
        if (user == null || !user.EmailConfirmed)
            return;

        var (plainToken, tokenHash) = _tokenGenerator.GenerateToken();
        user.PasswordResetTokenHash = tokenHash;
        user.PasswordResetTokenExpiresAt = DateTime.UtcNow.Add(PasswordResetTokenLifetime);
        user.UpdatedAt = DateTime.UtcNow;
        _userRepo.Update(user);

        var resetUrl = _urlBuilder.BuildPasswordResetUrl(user.Email, plainToken);
        var template = _emailTemplates.RenderPasswordReset(user.FullName, resetUrl);

        await _outboxRepo.AddAsync(new OutboxMessage
        {
            ToAddress = user.Email,
            Subject = template.Subject,
            BodyHtml = template.HtmlBody,
            BodyPlainText = template.PlainBody
        }, ct);

        await _userRepo.SaveChangesAsync(ct);

        _logger.LogInformation("Password reset requested for user {UserId}", user.Id);
    }

    public async Task ResetPasswordAsync(ResetPasswordDto dto, CancellationToken ct = default)
    {
        await ValidateAsync(_resetValidator, dto, ct);

        var email = NormalizeEmail(dto.Email);
        var user = await _userRepo.GetByEmailAsync(email, ct);

        const string genericError = "Invalid or expired reset token.";

        if (user == null || user.PasswordResetTokenHash == null
            || user.PasswordResetTokenExpiresAt == null
            || user.PasswordResetTokenExpiresAt.Value < DateTime.UtcNow)
        {
            throw new UnauthorizedException(genericError);
        }

        if (!_tokenGenerator.Verify(dto.Token, user.PasswordResetTokenHash))
            throw new UnauthorizedException(genericError);

        user.PasswordHash = _passwordHasher.Hash(dto.NewPassword);
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresAt = null;
        user.FailedLoginAttempts = 0;
        user.LockoutUntil = null;
        user.UpdatedAt = DateTime.UtcNow;
        _userRepo.Update(user);

        // Безопасность: после смены пароля все refresh-токены отозвать
        await _refreshTokenRepo.RevokeAllForUserAsync(user.Id, ct);
        await _userRepo.SaveChangesAsync(ct);

        _logger.LogInformation("Password reset successfully for user {UserId}", user.Id);
    }

    // ───────────────────────── helpers ─────────────────────────

    private async Task EnqueueConfirmationEmailAsync(User user, string plainToken, CancellationToken ct)
    {
        var url = _urlBuilder.BuildEmailConfirmationUrl(user.Email, plainToken);
        var template = _emailTemplates.RenderConfirmation(user.FullName, url);

        await _outboxRepo.AddAsync(new OutboxMessage
        {
            ToAddress = user.Email,
            Subject = template.Subject,
            BodyHtml = template.HtmlBody,
            BodyPlainText = template.PlainBody
        }, ct);
    }

    private async Task<AuthResultDto> GenerateTokensAsync(User user, CancellationToken ct)
    {
        var accessToken = _jwtService.GenerateAccessToken(user.Id, user.Email, user.Role.ToString());
        var refreshTokenValue = _jwtService.GenerateRefreshToken();
        var expiresAt = _jwtService.GetExpiresAtUnix();

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            Token = refreshTokenValue,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        await _refreshTokenRepo.AddAsync(refreshToken, ct);
        await _refreshTokenRepo.SaveChangesAsync(ct);

        return new AuthResultDto(accessToken, refreshTokenValue, expiresAt);
    }

    private static string NormalizeEmail(string email)
        => email.Trim().ToLowerInvariant();

    private static async Task ValidateAsync<T>(IValidator<T> validator, T dto, CancellationToken ct)
    {
        var result = await validator.ValidateAsync(dto, ct);
        if (!result.IsValid)
            throw new DomainValidationException(
                result.Errors.GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
    }
}