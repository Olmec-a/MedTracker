using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MedTracker.Application.DTOs;
using MedTracker.Application.Interfaces;
using MedTracker.Grpc.Interceptors;
using MedTracker.Grpc.Protos;

namespace MedTracker.Grpc.Services;

public class AuthGrpcService : AuthService.AuthServiceBase
{
    private readonly IAuthService _authService;

    public AuthGrpcService(IAuthService authService)
    {
        _authService = authService;
    }

    public override async Task<AuthResponse> Register(RegisterRequest request, ServerCallContext context)
    {
        var dto = new RegisterDto(request.Login, request.Password, request.FullName, request.Age);
        var result = await _authService.RegisterAsync(dto, context.CancellationToken);
        return ToResponse(result);
    }

    public override async Task<AuthResponse> Login(LoginRequest request, ServerCallContext context)
    {
        var dto = new LoginDto(request.Login, request.Password);
        var result = await _authService.LoginAsync(dto, context.CancellationToken);
        return ToResponse(result);
    }

    public override async Task<AuthResponse> RefreshToken(RefreshTokenRequest request, ServerCallContext context)
    {
        var result = await _authService.RefreshTokenAsync(request.RefreshToken, context.CancellationToken);
        return ToResponse(result);
    }

    public override async Task<Empty> ChangePassword(ChangePasswordRequest request, ServerCallContext context)
    {
        var userId = context.GetUserId();
        var dto = new ChangePasswordDto(request.CurrentPassword, request.NewPassword);
        await _authService.ChangePasswordAsync(userId, dto, context.CancellationToken);
        return new Empty();
    }

    public override async Task<Empty> Logout(Empty request, ServerCallContext context)
    {
        var userId = context.GetUserId();
        await _authService.LogoutAsync(userId, context.CancellationToken);
        return new Empty();
    }

    private static AuthResponse ToResponse(AuthResultDto dto) => new()
    {
        AccessToken = dto.AccessToken,
        RefreshToken = dto.RefreshToken,
        ExpiresAt = dto.ExpiresAt
    };
}