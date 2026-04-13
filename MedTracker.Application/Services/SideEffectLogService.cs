using FluentValidation;
using MedTracker.Application.DTOs;
using MedTracker.Application.Interfaces;
using MedTracker.Domain.Entities;
using MedTracker.Domain.Exceptions;

namespace MedTracker.Application.Services;

public class SideEffectLogService : ISideEffectLogService
{
    private readonly IUserSideEffectLogRepository _logRepo;
    private readonly ISideEffectRepository _sideEffectRepo;
    private readonly IValidator<CreateSideEffectLogDto> _validator;

    public SideEffectLogService(
        IUserSideEffectLogRepository logRepo,
        ISideEffectRepository sideEffectRepo,
        IValidator<CreateSideEffectLogDto> validator)
    {
        _logRepo = logRepo;
        _sideEffectRepo = sideEffectRepo;
        _validator = validator;
    }

    public async Task<SideEffectLogDto> LogSideEffectAsync(Guid userId, CreateSideEffectLogDto dto, CancellationToken ct = default)
    {
        var validation = await _validator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
            throw new DomainValidationException(
                validation.Errors.GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

        var sideEffect = await _sideEffectRepo.GetByIdAsync(dto.SideEffectId, ct)
            ?? throw new NotFoundException(nameof(SideEffect), dto.SideEffectId);

        var log = new UserSideEffectLog
        {
            UserId = userId,
            SideEffectId = dto.SideEffectId,
            Date = dto.Date,
            Intensity = dto.Intensity,
            Comment = dto.Comment
        };

        await _logRepo.AddAsync(log, ct);
        await _logRepo.SaveChangesAsync(ct);

        return new SideEffectLogDto(log.Id, sideEffect.Id, sideEffect.Name, log.Date, log.Intensity, log.Comment);
    }

    public async Task<PaginatedResultDto<SideEffectLogDto>> GetLogsAsync(
        Guid userId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _logRepo.GetByUserIdAsync(userId, from, to, page, pageSize, ct);
        var dtos = items.Select(l => new SideEffectLogDto(
            l.Id, l.SideEffectId, l.SideEffect.Name, l.Date, l.Intensity, l.Comment)).ToList();
        return new PaginatedResultDto<SideEffectLogDto>(dtos, total);
    }

    public async Task DeleteLogAsync(Guid userId, Guid logId, CancellationToken ct = default)
    {
        var log = await _logRepo.GetByIdAsync(logId, ct)
            ?? throw new NotFoundException(nameof(UserSideEffectLog), logId);

        if (log.UserId != userId)
            throw new ForbiddenException();

        log.IsDeleted = true;
        log.DeletedAt = DateTime.UtcNow;
        _logRepo.Update(log);
        await _logRepo.SaveChangesAsync(ct);
    }
}