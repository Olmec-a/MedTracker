using FluentValidation;
using MedTracker.Application.DTOs;
using MedTracker.Application.Interfaces;
using MedTracker.Domain.Entities;
using MedTracker.Domain.Exceptions;

namespace MedTracker.Application.Services;

public class UserMedicationService : IUserMedicationService
{
    private readonly IUserMedicationRepository _userMedRepo;
    private readonly IUserSupplementRepository _userSuppRepo;
    private readonly IMedicationRepository _medicationRepo;
    private readonly ISupplementRepository _supplementRepo;
    private readonly IValidator<AssignMedicationDto> _assignMedValidator;
    private readonly IValidator<AssignSupplementDto> _assignSuppValidator;

    public UserMedicationService(
        IUserMedicationRepository userMedRepo,
        IUserSupplementRepository userSuppRepo,
        IMedicationRepository medicationRepo,
        ISupplementRepository supplementRepo,
        IValidator<AssignMedicationDto> assignMedValidator,
        IValidator<AssignSupplementDto> assignSuppValidator)
    {
        _userMedRepo = userMedRepo;
        _userSuppRepo = userSuppRepo;
        _medicationRepo = medicationRepo;
        _supplementRepo = supplementRepo;
        _assignMedValidator = assignMedValidator;
        _assignSuppValidator = assignSuppValidator;
    }

    public async Task<UserMedicationDto> AssignMedicationAsync(Guid userId, AssignMedicationDto dto, CancellationToken ct = default)
    {
        var validation = await _assignMedValidator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
            throw new DomainValidationException(
                validation.Errors.GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

        var medication = await _medicationRepo.GetByIdAsync(dto.MedicationId, ct)
            ?? throw new NotFoundException(nameof(Medication), dto.MedicationId);

        var userMedication = new UserMedication
        {
            UserId = userId,
            MedicationId = dto.MedicationId,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            IsActive = true
        };

        await _userMedRepo.AddAsync(userMedication, ct);
        await _userMedRepo.SaveChangesAsync(ct);

        return new UserMedicationDto(
            userMedication.Id, medication.Id, medication.TradeName, medication.INN,
            userMedication.StartDate, userMedication.EndDate, userMedication.IsActive);
    }

    public async Task RemoveMedicationAsync(Guid userId, Guid userMedicationId, CancellationToken ct = default)
    {
        var entity = await _userMedRepo.GetByIdAsync(userMedicationId, ct)
            ?? throw new NotFoundException(nameof(UserMedication), userMedicationId);

        if (entity.UserId != userId)
            throw new ForbiddenException();

        entity.IsActive = false;
        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        _userMedRepo.Update(entity);
        await _userMedRepo.SaveChangesAsync(ct);
    }

    public async Task<List<UserMedicationDto>> GetUserMedicationsAsync(Guid userId, CancellationToken ct = default)
    {
        var items = await _userMedRepo.GetByUserIdAsync(userId, activeOnly: false, ct);
        return items.Select(um => new UserMedicationDto(
            um.Id, um.MedicationId,
            um.Medication.TradeName, um.Medication.INN,
            um.StartDate, um.EndDate, um.IsActive)).ToList();
    }

    public async Task<UserSupplementDto> AssignSupplementAsync(Guid userId, AssignSupplementDto dto, CancellationToken ct = default)
    {
        var validation = await _assignSuppValidator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
            throw new DomainValidationException(
                validation.Errors.GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

        var supplement = await _supplementRepo.GetByIdAsync(dto.SupplementId, ct)
            ?? throw new NotFoundException(nameof(Supplement), dto.SupplementId);

        var userSupplement = new UserSupplement
        {
            UserId = userId,
            SupplementId = dto.SupplementId,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            IsActive = true
        };

        await _userSuppRepo.AddAsync(userSupplement, ct);
        await _userSuppRepo.SaveChangesAsync(ct);

        return new UserSupplementDto(
            userSupplement.Id, supplement.Id, supplement.Name,
            userSupplement.StartDate, userSupplement.EndDate, userSupplement.IsActive);
    }

    public async Task RemoveSupplementAsync(Guid userId, Guid userSupplementId, CancellationToken ct = default)
    {
        var entity = await _userSuppRepo.GetByIdAsync(userSupplementId, ct)
            ?? throw new NotFoundException(nameof(UserSupplement), userSupplementId);

        if (entity.UserId != userId)
            throw new ForbiddenException();

        entity.IsActive = false;
        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        _userSuppRepo.Update(entity);
        await _userSuppRepo.SaveChangesAsync(ct);
    }

    public async Task<List<UserSupplementDto>> GetUserSupplementsAsync(Guid userId, CancellationToken ct = default)
    {
        var items = await _userSuppRepo.GetByUserIdAsync(userId, activeOnly: false, ct);
        return items.Select(us => new UserSupplementDto(
            us.Id, us.SupplementId, us.Supplement.Name,
            us.StartDate, us.EndDate, us.IsActive)).ToList();
    }
}