using FluentValidation;
using MedTracker.Application.DTOs;
using MedTracker.Application.Interfaces;
using MedTracker.Domain.Entities;
using MedTracker.Domain.Exceptions;

namespace MedTracker.Application.Services;

public class UserProfileService : IUserProfileService
{
    private readonly IUserRepository _userRepo;
    private readonly IDiagnosisRepository _diagnosisRepo;
    private readonly IUserDiagnosisRepository _userDiagnosisRepo;
    private readonly IValidator<UpdateProfileDto> _updateValidator;

    public UserProfileService(
        IUserRepository userRepo,
        IDiagnosisRepository diagnosisRepo,
        IUserDiagnosisRepository userDiagnosisRepo,
        IValidator<UpdateProfileDto> updateValidator)
    {
        _userRepo = userRepo;
        _diagnosisRepo = diagnosisRepo;
        _userDiagnosisRepo = userDiagnosisRepo;
        _updateValidator = updateValidator;
    }

    public async Task<UserProfileDto> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _userRepo.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException(nameof(User), userId);

        return ToDto(user);
    }

    public async Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateProfileDto dto, CancellationToken ct = default)
    {
        var validation = await _updateValidator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
            throw new DomainValidationException(
                validation.Errors.GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

        var user = await _userRepo.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException(nameof(User), userId);

        user.FullName = dto.FullName;
        user.Age = dto.Age;
        user.UpdatedAt = DateTime.UtcNow;

        _userRepo.Update(user);
        await _userRepo.SaveChangesAsync(ct);

        return ToDto(user);
    }

    public async Task<List<UserDiagnosisDto>> AssignDiagnosesAsync(Guid userId, List<Guid> diagnosisIds, CancellationToken ct = default)
    {
        _ = await _userRepo.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException(nameof(User), userId);

        var diagnoses = await _diagnosisRepo.GetByIdsAsync(diagnosisIds, ct);
        if (diagnoses.Count != diagnosisIds.Count)
            throw new NotFoundException(nameof(Diagnosis), "one or more IDs");

        await _userDiagnosisRepo.RemoveByUserIdAsync(userId, ct);

        var entries = diagnoses.Select(d => new UserDiagnosis
        {
            UserId = userId,
            DiagnosisId = d.Id,
            AssignedAt = DateTime.UtcNow
        }).ToList();

        await _userDiagnosisRepo.AddRangeAsync(entries, ct);
        await _userDiagnosisRepo.SaveChangesAsync(ct);

        return diagnoses.Select(d => new UserDiagnosisDto(d.Id, d.Name, DateTime.UtcNow)).ToList();
    }

    public async Task<List<UserDiagnosisDto>> GetUserDiagnosesAsync(Guid userId, CancellationToken ct = default)
    {
        var entries = await _userDiagnosisRepo.GetByUserIdAsync(userId, ct);
        return entries.Select(e => new UserDiagnosisDto(e.DiagnosisId, e.Diagnosis.Name, e.AssignedAt)).ToList();
    }

    private static UserProfileDto ToDto(User u)
        => new(u.Id, u.Email, u.FullName, u.Age, u.EmailConfirmed, u.CreatedAt, u.UpdatedAt);
}