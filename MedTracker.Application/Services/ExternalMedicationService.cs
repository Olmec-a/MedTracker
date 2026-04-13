using FluentValidation;
using MedTracker.Application.DTOs;
using MedTracker.Application.Interfaces;
using MedTracker.Domain.Entities;
using MedTracker.Domain.Exceptions;

namespace MedTracker.Application.Services;

public class ExternalMedicationService : IExternalMedicationService
{
    private readonly IExternalMedicationRepository _repo;
    private readonly IValidator<CreateExternalMedicationDto> _validator;

    public ExternalMedicationService(
        IExternalMedicationRepository repo,
        IValidator<CreateExternalMedicationDto> validator)
    {
        _repo = repo;
        _validator = validator;
    }

    public async Task<ExternalMedicationDto> AddAsync(Guid userId, CreateExternalMedicationDto dto, CancellationToken ct = default)
    {
        var validation = await _validator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
            throw new DomainValidationException(
                validation.Errors.GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

        var entity = new ExternalMedication
        {
            UserId = userId,
            Name = dto.Name,
            Dosage = dto.Dosage,
            Date = dto.Date,
            Comment = dto.Comment
        };

        await _repo.AddAsync(entity, ct);
        await _repo.SaveChangesAsync(ct);

        return new ExternalMedicationDto(entity.Id, entity.Name, entity.Dosage, entity.Date, entity.Comment);
    }

    public async Task<PaginatedResultDto<ExternalMedicationDto>> GetAsync(
        Guid userId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _repo.GetByUserIdAsync(userId, from, to, page, pageSize, ct);
        var dtos = items.Select(e =>
            new ExternalMedicationDto(e.Id, e.Name, e.Dosage, e.Date, e.Comment)).ToList();
        return new PaginatedResultDto<ExternalMedicationDto>(dtos, total);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct)
            ?? throw new NotFoundException(nameof(ExternalMedication), id);

        if (entity.UserId != userId)
            throw new ForbiddenException();

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        _repo.Update(entity);
        await _repo.SaveChangesAsync(ct);
    }
}