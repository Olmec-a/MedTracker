using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MedTracker.Application.Interfaces;
using MedTracker.Grpc.Interceptors;
using MedTracker.Grpc.Protos;
using AppDtos = MedTracker.Application.DTOs;

namespace MedTracker.Grpc.Services;

public class UserProfileGrpcService : UserProfileService.UserProfileServiceBase
{
    private readonly IUserProfileService _service;

    public UserProfileGrpcService(IUserProfileService service)
    {
        _service = service;
    }

    public override async Task<UserProfileResponse> GetProfile(Empty request, ServerCallContext context)
    {
        var userId = context.GetUserId();
        var profile = await _service.GetProfileAsync(userId, context.CancellationToken);
        return ToResponse(profile);
    }

    public override async Task<UserProfileResponse> UpdateProfile(UpdateProfileRequest request, ServerCallContext context)
    {
        var userId = context.GetUserId();
        var dto = new AppDtos.UpdateProfileDto(request.FullName, request.Age);
        var profile = await _service.UpdateProfileAsync(userId, dto, context.CancellationToken);
        return ToResponse(profile);
    }

    public override async Task<UserDiagnosesResponse> AssignDiagnoses(AssignDiagnosesRequest request, ServerCallContext context)
    {
        var userId = context.GetUserId();
        var diagnosisIds = request.DiagnosisIds.Select(Guid.Parse).ToList();
        var diagnoses = await _service.AssignDiagnosesAsync(userId, diagnosisIds, context.CancellationToken);
        return ToResponse(diagnoses);
    }

    public override async Task<UserDiagnosesResponse> GetMyDiagnoses(Empty request, ServerCallContext context)
    {
        var userId = context.GetUserId();
        var diagnoses = await _service.GetUserDiagnosesAsync(userId, context.CancellationToken);
        return ToResponse(diagnoses);
    }

    private static UserProfileResponse ToResponse(AppDtos.UserProfileDto dto) => new()
    {
        Id = dto.Id.ToString(),
        Login = dto.Login,
        FullName = dto.FullName,
        Age = dto.Age,
        CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(dto.CreatedAt, DateTimeKind.Utc)),
        UpdatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(dto.UpdatedAt, DateTimeKind.Utc))
    };

    private static UserDiagnosesResponse ToResponse(List<AppDtos.UserDiagnosisDto> dtos)
    {
        var response = new UserDiagnosesResponse();
        response.Diagnoses.AddRange(dtos.Select(d => new Protos.UserDiagnosisDto
        {
            DiagnosisId = d.DiagnosisId.ToString(),
            DiagnosisName = d.DiagnosisName,
            AssignedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(d.AssignedAt, DateTimeKind.Utc))
        }));
        return response;
    }
}