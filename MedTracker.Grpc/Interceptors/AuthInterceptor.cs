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
        "/medtracker.AuthService/RefreshToken",
        "/medtracker.AuthService/ConfirmEmail",
        "/medtracker.AuthService/ResendConfirmation",
        "/medtracker.AuthService/RequestPasswordReset",
        "/medtracker.AuthService/ResetPassword"
    };

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

        var userIdStr = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? principal.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid user ID in token."));

        var role = principal.FindFirst(ClaimTypes.Role)?.Value ?? "User";

        if (AdminMethods.Contains(method) && !string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Admin role required."));

        await CheckUserStatusAsync(userId);

        context.UserState["UserId"] = userIdStr;
        context.UserState["UserRole"] = role;
    }

    private async Task CheckUserStatusAsync(Guid userId)
    {
        var status = await _userStatusCache.GetOrCreateAsync($"user-status:{userId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var user = await repo.GetByIdAsync(userId);
            return new UserStatus(user != null, user?.LockoutUntil);
        });

        if (status == null || !status.Exists)
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