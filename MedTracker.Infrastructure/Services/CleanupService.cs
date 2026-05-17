using MedTracker.Application.Interfaces;
using MedTracker.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MedTracker.Infrastructure.Services;

public class CleanupService : ICleanupService
{
    private readonly AppDbContext _db;
    private readonly ILogger<CleanupService> _logger;

    public CleanupService(AppDbContext db, ILogger<CleanupService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int> CleanupExpiredRefreshTokensAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var deleted = await _db.RefreshTokens
            .Where(rt => rt.ExpiresAt < now || rt.IsRevoked)
            .ExecuteDeleteAsync(ct);
        if (deleted > 0)
            _logger.LogInformation("Cleanup: deleted {Count} expired/revoked refresh tokens", deleted);
        return deleted;
    }

    public async Task<int> CleanupExpiredEmailConfirmationTokensAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var updated = await _db.Users
            .Where(u => u.EmailConfirmationTokenHash != null
                        && u.EmailConfirmationTokenExpiresAt != null
                        && u.EmailConfirmationTokenExpiresAt < now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.EmailConfirmationTokenHash, (string?)null)
                .SetProperty(u => u.EmailConfirmationTokenExpiresAt, (DateTime?)null), ct);
        if (updated > 0)
            _logger.LogInformation("Cleanup: cleared {Count} expired email confirmation tokens", updated);
        return updated;
    }

    public async Task<int> CleanupExpiredPasswordResetTokensAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var updated = await _db.Users
            .Where(u => u.PasswordResetTokenHash != null
                        && u.PasswordResetTokenExpiresAt != null
                        && u.PasswordResetTokenExpiresAt < now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.PasswordResetTokenHash, (string?)null)
                .SetProperty(u => u.PasswordResetTokenExpiresAt, (DateTime?)null), ct);
        if (updated > 0)
            _logger.LogInformation("Cleanup: cleared {Count} expired password reset tokens", updated);
        return updated;
    }
}