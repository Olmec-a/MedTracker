using FluentValidation;
using MedTracker.Application.DTOs;
using MedTracker.Application.Interfaces;
using MedTracker.Domain.Entities;
using MedTracker.Domain.Exceptions;

namespace MedTracker.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly IJwtService _jwtService;
    private readonly IValidator<RegisterDto> _registerValidator;
    private readonly IValidator<LoginDto> _loginValidator;

    public AuthService(
        IUserRepository userRepo,
        IRefreshTokenRepository refreshTokenRepo,
        IJwtService jwtService,
        IValidator<RegisterDto> registerValidator,
        IValidator<LoginDto> loginValidator)
    {
        _userRepo = userRepo;
        _refreshTokenRepo = refreshTokenRepo;
        _jwtService = jwtService;
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
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
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

        if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            throw new UnauthorizedException("Invalid login or password.");

        return await GenerateTokensAsync(user, ct);
    }

    public async Task<AuthResultDto> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var storedToken = await _refreshTokenRepo.GetByTokenAsync(refreshToken, ct)
            ?? throw new UnauthorizedException("Invalid refresh token.");

        if (storedToken.IsRevoked || storedToken.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedException("Refresh token is expired or revoked.");

        storedToken.IsRevoked = true;
        storedToken.RevokedAt = DateTime.UtcNow;
        _refreshTokenRepo.Update(storedToken);

        var user = await _userRepo.GetByIdAsync(storedToken.UserId, ct)
            ?? throw new NotFoundException(nameof(User), storedToken.UserId);

        return await GenerateTokensAsync(user, ct);
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