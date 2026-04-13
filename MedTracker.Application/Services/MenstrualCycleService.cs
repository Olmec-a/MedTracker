using FluentValidation;
using MedTracker.Application.DTOs;
using MedTracker.Application.Interfaces;
using MedTracker.Domain.Entities;
using MedTracker.Domain.Exceptions;

namespace MedTracker.Application.Services;

public class MenstrualCycleService : IMenstrualCycleService
{
    private readonly IMenstrualCycleRepository _repo;
    private readonly IValidator<CreateCycleEntryDto> _createValidator;
    private readonly IValidator<UpdateCycleEntryDto> _updateValidator;

    public MenstrualCycleService(
        IMenstrualCycleRepository repo,
        IValidator<CreateCycleEntryDto> createValidator,
        IValidator<UpdateCycleEntryDto> updateValidator)
    {
        _repo = repo;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<MenstrualCycleDto> AddEntryAsync(Guid userId, CreateCycleEntryDto dto, CancellationToken ct = default)
    {
        var validation = await _createValidator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
            throw new DomainValidationException(
                validation.Errors.GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

        var entry = new MenstrualCycleEntry
        {
            UserId = userId,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            Intensity = dto.Intensity,
            Symptoms = dto.Symptoms,
            Notes = dto.Notes
        };

        await _repo.AddAsync(entry, ct);
        await _repo.SaveChangesAsync(ct);

        return MapToDto(entry);
    }

    public async Task<MenstrualCycleDto> UpdateEntryAsync(Guid userId, UpdateCycleEntryDto dto, CancellationToken ct = default)
    {
        var validation = await _updateValidator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
            throw new DomainValidationException(
                validation.Errors.GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

        var entry = await _repo.GetByIdAsync(dto.Id, ct)
            ?? throw new NotFoundException(nameof(MenstrualCycleEntry), dto.Id);

        if (entry.UserId != userId)
            throw new ForbiddenException();

        entry.StartDate = dto.StartDate;
        entry.EndDate = dto.EndDate;
        entry.Intensity = dto.Intensity;
        entry.Symptoms = dto.Symptoms;
        entry.Notes = dto.Notes;
        entry.UpdatedAt = DateTime.UtcNow;

        _repo.Update(entry);
        await _repo.SaveChangesAsync(ct);

        return MapToDto(entry);
    }

    public async Task<PaginatedResultDto<MenstrualCycleDto>> GetHistoryAsync(
        Guid userId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _repo.GetByUserIdAsync(userId, from, to, page, pageSize, ct);
        var dtos = items.Select(MapToDto).ToList();
        return new PaginatedResultDto<MenstrualCycleDto>(dtos, total);
    }

    public async Task DeleteEntryAsync(Guid userId, Guid entryId, CancellationToken ct = default)
    {
        var entry = await _repo.GetByIdAsync(entryId, ct)
            ?? throw new NotFoundException(nameof(MenstrualCycleEntry), entryId);

        if (entry.UserId != userId)
            throw new ForbiddenException();

        entry.IsDeleted = true;
        entry.DeletedAt = DateTime.UtcNow;
        _repo.Update(entry);
        await _repo.SaveChangesAsync(ct);
    }

    private static MenstrualCycleDto MapToDto(MenstrualCycleEntry e) =>
        new(e.Id, e.StartDate, e.EndDate, e.Intensity, e.Symptoms, e.Notes);
}