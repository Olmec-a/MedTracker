using MedTracker.Domain.Entities;

namespace MedTracker.Application.Interfaces;

public interface IRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<T>> GetAllAsync(CancellationToken ct = default);
    Task<T> AddAsync(T entity, CancellationToken ct = default);
    void Update(T entity);
    void Remove(T entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByLoginAsync(string login, CancellationToken ct = default);
    Task<bool> ExistsByLoginAsync(string login, CancellationToken ct = default);
}

public interface IDiagnosisRepository : IRepository<Diagnosis>
{
    Task<List<Diagnosis>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
}

public interface IMedicationRepository : IRepository<Medication>
{
    Task<(List<Medication> Items, int TotalCount)> GetByDiagnosisIdAsync(Guid diagnosisId, int page, int pageSize, CancellationToken ct = default);
}

public interface ISupplementRepository : IRepository<Supplement>
{
    Task<(List<Supplement> Items, int TotalCount)> GetByMedicationIdAsync(Guid medicationId, int page, int pageSize, CancellationToken ct = default);
}

public interface ISideEffectRepository : IRepository<SideEffect>
{
    Task<(List<SideEffect> Items, int TotalCount)> GetByMedicationIdAsync(Guid medicationId, int page, int pageSize, CancellationToken ct = default);
}

public interface IUserDiagnosisRepository
{
    Task<List<UserDiagnosis>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<UserDiagnosis> entities, CancellationToken ct = default);
    Task RemoveByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IUserMedicationRepository : IRepository<UserMedication>
{
    Task<List<UserMedication>> GetByUserIdAsync(Guid userId, bool activeOnly = false, CancellationToken ct = default);
}

public interface IUserSupplementRepository : IRepository<UserSupplement>
{
    Task<List<UserSupplement>> GetByUserIdAsync(Guid userId, bool activeOnly = false, CancellationToken ct = default);
}

public interface IUserSideEffectLogRepository : IRepository<UserSideEffectLog>
{
    Task<(List<UserSideEffectLog> Items, int TotalCount)> GetByUserIdAsync(
        Guid userId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default);
}

public interface IExternalMedicationRepository : IRepository<ExternalMedication>
{
    Task<(List<ExternalMedication> Items, int TotalCount)> GetByUserIdAsync(
        Guid userId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default);
}

public interface IMenstrualCycleRepository : IRepository<MenstrualCycleEntry>
{
    Task<(List<MenstrualCycleEntry> Items, int TotalCount)> GetByUserIdAsync(
        Guid userId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default);

    Task<bool> HasOverlappingEntryAsync(Guid userId, DateTime startDate, DateTime? endDate, Guid? excludeId, CancellationToken ct = default);
}

public interface IRefreshTokenRepository : IRepository<RefreshToken>
{
    Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken ct = default);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default);
}

public interface IImportRecordRepository : IRepository<ImportRecord>
{
    Task<List<ImportRecord>> GetAllOrderedAsync(CancellationToken ct = default);
}