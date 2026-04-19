using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;
using MedTracker.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace MedTracker.Grpc.Interceptors;

public class AuthInterceptor : Interceptor
{
    private readonly IConfiguration _config;
    private readonly ILogger<AuthInterceptor> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMemoryCache _userStatusCache;

    // Methods that don't require authentication
    private static readonly HashSet<string> AnonymousMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "/medtracker.AuthService/Register",
        "/medtracker.AuthService/Login",
        "/medtracker.AuthService/RefreshToken"
    };

    // Methods that require Admin role
    private static readonly HashSet<string> AdminMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "/medtracker.AdminService/ImportMedicationData",
        "/medtracker.AdminService/GetImportHistory"
    };

    public AuthInterceptor(
        IConfiguration config,
        ILogger<AuthInterceptor> logger,
        IServiceProvider serviceProvider,
        IMemoryCache userStatusCache)
    {
        _config = config;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _userStatusCache = userStatusCache;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        await AuthorizeRequestAsync(context);
        return await continuation(request, context);
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await AuthorizeRequestAsync(context);
        return await continuation(requestStream, context);
    }

    private async Task AuthorizeRequestAsync(ServerCallContext context)
    {
        var method = context.Method;

        if (AnonymousMethods.Contains(method))
            return;

        var authHeader = context.RequestHeaders.GetValue("authorization");
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing or invalid authorization token."));

        var token = authHeader["Bearer ".Length..].Trim();
        var principal = ValidateToken(token);

        if (principal == null)
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid or expired token."));

        // Store claims in UserState for downstream access
        var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = principal.FindFirstValue(ClaimTypes.Role) ?? "User";

        context.UserState["UserId"] = userId!;
        context.UserState["UserRole"] = role;
        context.UserState["UserLogin"] = principal.FindFirstValue(JwtRegisteredClaimNames.UniqueName) ?? "";

        // Check admin role
        if (AdminMethods.Contains(method) && !string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Admin role is required."));

        // Verify user still exists and is not locked out (cached для производительности — 60s TTL)
        await CheckUserStatusAsync(Guid.Parse(userId!));
    }

    private async Task CheckUserStatusAsync(Guid userId)
    {
        var cacheKey = $"user-status:{userId}";

        var status = await _userStatusCache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);

            using var scope = _serviceProvider.CreateScope();
            var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var user = await userRepo.GetByIdAsync(userId);

            return user == null
                ? new UserStatus(Exists: false, LockoutUntil: null)
                : new UserStatus(Exists: true, LockoutUntil: user.LockoutUntil);
        });

        if (status is null || !status.Exists)
            throw new RpcException(new Status(StatusCode.Unauthenticated, "User no longer exists."));

        if (status.LockoutUntil.HasValue && status.LockoutUntil.Value > DateTime.UtcNow)
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Account is locked."));
    }

    private record UserStatus(bool Exists, DateTime? LockoutUntil);

    private ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var secret = _config["Jwt:Secret"]!;
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _config["Jwt:Issuer"],
                ValidAudience = _config["Jwt:Audience"],
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.Zero
            };

            var handler = new JwtSecurityTokenHandler();
            return handler.ValidateToken(token, parameters, out _);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return null;
        }
    }
}

// Extension to extract user info from ServerCallContext
public static class ServerCallContextExtensions
{
    public static Guid GetUserId(this ServerCallContext context)
    {
        if (context.UserState.TryGetValue("UserId", out var userIdObj) && userIdObj is string userIdStr)
            return Guid.Parse(userIdStr);
        throw new RpcException(new Status(StatusCode.Unauthenticated, "User ID not found in context."));
    }

    public static string GetUserRole(this ServerCallContext context)
    {
        if (context.UserState.TryGetValue("UserRole", out var roleObj) && roleObj is string role)
            return role;
        return "User";
    }
}