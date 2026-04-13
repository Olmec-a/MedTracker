using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MedTracker.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace MedTracker.Infrastructure.Services;

public class JwtService : IJwtService
{
    private readonly IConfiguration _config;
    private readonly int _accessTokenLifetimeMinutes;

    public JwtService(IConfiguration config)
    {
        _config = config;
        _accessTokenLifetimeMinutes = int.Parse(config["Jwt:AccessTokenLifetimeMinutes"] ?? "15");
    }

    public string GenerateAccessToken(Guid userId, string login, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            _config["Jwt:Secret"] ?? throw new InvalidOperationException("JWT secret is not configured.")));

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, login),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_accessTokenLifetimeMinutes);

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var bytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    public long GetExpiresAtUnix()
    {
        return new DateTimeOffset(DateTime.UtcNow.AddMinutes(_accessTokenLifetimeMinutes)).ToUnixTimeSeconds();
    }
}