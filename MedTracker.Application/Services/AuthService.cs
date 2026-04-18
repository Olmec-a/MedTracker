using FluentValidation;
using MedTracker.Application.DTOs;
using MedTracker.Application.Interfaces;
using MedTracker.Domain.Entities;
using MedTracker.Domain.Exceptions;

namespace MedTracker.Application.Services;

public class AuthService : IAuthService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly IUserRepository _userRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly IJwtService _jwtService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IValidator<RegisterDto> _registerValidator;
    private readonly IValidator<LoginDto> _loginValidator;

    public AuthService(
        IUserRepository userRepo,
        IRefreshTokenRepository refreshTokenRepo,
        IJwtService jwtService,
        IPasswordHasher passwordHasher,
        IValidator<RegisterDto> registerValidator,
        IValidator<LoginDto> loginValidator)
    {
        _userRepo = userRepo;
        _refreshTokenRepo = refreshTokenRepo;
        _jwtService = jwtService;
        _passwordHasher = passwordHasher;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
    }

    public async Task<AuthResultDto> RegisterAsync(RegisterDto dto, CancellationToken ct = default)
    {
        var validation = await _registerValidator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
            throw new DomainValidationException(
                validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

        if (await _userRepo.ExistsByLoginAsync(dto.Login, ct))
            throw new DuplicateException($"User with login '{dto.Login}' already exists.");

        var user = new User
        {
            Login = dto.Login,
            PasswordHash = _passwordHasher.Hash(dto.Password),
            FullName = dto.FullName,
            Age = dto.Age
        };

        await _userRepo.AddAsync(user, ct);
        await _userRepo.SaveChangesAsync(ct);

        return await GenerateTokensAsync(user, ct);
    }

    public async Task<AuthResultDto> LoginAsync(LoginDto dto, CancellationToken ct = default)
    {
        var validation = await _loginValidator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
            throw new DomainValidationException(
                validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

        var user = await _userRepo.GetByLoginAsync(dto.Login, ct)
            ?? throw new UnauthorizedException("Invalid login or password.");

        // Check lockout
        if (user.LockoutUntil.HasValue && user.LockoutUntil.Value > DateTime.UtcNow)
        {
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
                throw new UnauthorizedException($"Too many failed attempts. Account locked for {LockoutDuration.TotalMinutes} minutes.");
            }

            _userRepo.Update(user);
            await _userRepo.SaveChangesAsync(ct);
            throw new UnauthorizedException("Invalid login or password.");
        }

        // Reset counters on successful login
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

        // REPLAY DETECTION: если токен уже отозван — это попытка повторного использования.
        // Инвалидируем все токены пользователя — потенциально компрометация.
        if (storedToken.IsRevoked)
        {
            await _refreshTokenRepo.RevokeAllForUserAsync(storedToken.UserId, ct);
            await _refreshTokenRepo.SaveChangesAsync(ct);
            throw new UnauthorizedException("Refresh token replay detected. All sessions have been terminated.");
        }

        if (storedToken.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedException("Refresh token is expired.");

        var user = await _userRepo.GetByIdAsync(storedToken.UserId, ct)
            ?? throw new NotFoundException(nameof(User), storedToken.UserId);

        // Rotate: отзываем старый, создаём новый, связываем в цепочку
        var newRefreshToken = new RefreshToken
        {
            UserId = user.Id,
            Token = _jwtService.GenerateRefreshToken(),
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        await _refreshTokenRepo.AddAsync(newRefreshToken, ct);

        storedToken.IsRevoked = true;
        storedToken.RevokedAt = DateTime.UtcNow;
        storedToken.ReplacedByTokenId = newRefreshToken.Id;
        _refreshTokenRepo.Update(storedToken);

        await _refreshTokenRepo.SaveChangesAsync(ct);

        var accessToken = _jwtService.GenerateAccessToken(user.Id, user.Login, user.Role.ToString());
        return new AuthResultDto(accessToken, newRefreshToken.Token, _jwtService.GetExpiresAtUnix());
    }

    private async Task<AuthResultDto> GenerateTokensAsync(User user, CancellationToken ct)
    {
        var accessToken = _jwtService.GenerateAccessToken(user.Id, user.Login, user.Role.ToString());
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
}