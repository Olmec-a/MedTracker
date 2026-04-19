using MedTracker.Application.Interfaces;
using MedTracker.Domain.Entities;
using MedTracker.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MedTracker.Infrastructure.Repositories;

public class Repository<T> : IRepository<T> where T : BaseEntity
{
    protected readonly AppDbContext Db;
    protected readonly DbSet<T> Set;

    public Repository(AppDbContext db)
    {
        Db = db;
        Set = db.Set<T>();
    }

    public virtual async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await Set.FindAsync([id], ct);

    public virtual async Task<List<T>> GetAllAsync(CancellationToken ct = default)
        => await Set.ToListAsync(ct);

    public async Task<T> AddAsync(T entity, CancellationToken ct = default)
    {
        await Set.AddAsync(entity, ct);
        return entity;
    }

    public void Update(T entity) => Set.Update(entity);
    public void Remove(T entity) => Set.Remove(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await Db.SaveChangesAsync(ct);
}

// ── User ──
public class UserRepository : Repository<User>, IUserRepository
{
    public UserRepository(AppDbContext db) : base(db) { }

    public async Task<User?> GetByLoginAsync(string login, CancellationToken ct = default)
        => await Set.FirstOrDefaultAsync(u => u.Login == login, ct);

    public async Task<bool> ExistsByLoginAsync(string login, CancellationToken ct = default)
        => await Set.AnyAsync(u => u.Login == login, ct);
}

// ── Diagnosis ──
public class DiagnosisRepository : Repository<Diagnosis>, IDiagnosisRepository
{
    public DiagnosisRepository(AppDbContext db) : base(db) { }

    public async Task<List<Diagnosis>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
        => await Set.Where(d => ids.Contains(d.Id)).ToListAsync(ct);
}

// ── Medication ──
public class MedicationRepository : Repository<Medication>, IMedicationRepository
{
    public MedicationRepository(AppDbContext db) : base(db) { }

    public async Task<(List<Medication> Items, int TotalCount)> GetByDiagnosisIdAsync(Guid diagnosisId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Set.Where(m => m.DiagnosisId == diagnosisId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(m => m.TradeName)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }
}

// ── Supplement ──
public class SupplementRepository : Repository<Supplement>, ISupplementRepository
{
    public SupplementRepository(AppDbContext db) : base(db) { }

    public async Task<(List<Supplement> Items, int TotalCount)> GetByMedicationIdAsync(Guid medicationId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Set.Where(s => s.MedicationId == medicationId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(s => s.Name)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }
}

// ── SideEffect ──
public class SideEffectRepository : Repository<SideEffect>, ISideEffectRepository
{
    public SideEffectRepository(AppDbContext db) : base(db) { }

    public async Task<(List<SideEffect> Items, int TotalCount)> GetByMedicationIdAsync(Guid medicationId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Set.Where(se => se.MedicationId == medicationId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(se => se.Name)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }
}

// ── UserDiagnosis ──
public class UserDiagnosisRepository : IUserDiagnosisRepository
{
    private readonly AppDbContext _db;

    public UserDiagnosisRepository(AppDbContext db) => _db = db;

    public async Task<List<UserDiagnosis>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => await _db.UserDiagnoses
            .Include(ud => ud.Diagnosis)
            .Where(ud => ud.UserId == userId)
            .ToListAsync(ct);

    public async Task AddRangeAsync(IEnumerable<UserDiagnosis> entities, CancellationToken ct = default)
        => await _db.UserDiagnoses.AddRangeAsync(entities, ct);

    public async Task RemoveByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var existing = await _db.UserDiagnoses.Where(ud => ud.UserId == userId).ToListAsync(ct);
        _db.UserDiagnoses.RemoveRange(existing);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _db.SaveChangesAsync(ct);
}

// ── UserMedication ──
public class UserMedicationRepository : Repository<UserMedication>, IUserMedicationRepository
{
    public UserMedicationRepository(AppDbContext db) : base(db) { }

    public async Task<List<UserMedication>> GetByUserIdAsync(Guid userId, bool activeOnly = false, CancellationToken ct = default)
    {
        var query = Set.AsNoTracking().Include(um => um.Medication).Where(um => um.UserId == userId);
        if (activeOnly) query = query.Where(um => um.IsActive);
        return await query.OrderByDescending(um => um.StartDate).ToListAsync(ct);
    }
}

// ── UserSupplement ──
public class UserSupplementRepository : Repository<UserSupplement>, IUserSupplementRepository
{
    public UserSupplementRepository(AppDbContext db) : base(db) { }

    public async Task<List<UserSupplement>> GetByUserIdAsync(Guid userId, bool activeOnly = false, CancellationToken ct = default)
    {
        var query = Set.AsNoTracking().Include(us => us.Supplement).Where(us => us.UserId == userId);
        if (activeOnly) query = query.Where(us => us.IsActive);
        return await query.OrderByDescending(us => us.StartDate).ToListAsync(ct);
    }
}

// ── UserSideEffectLog ──
public class UserSideEffectLogRepository : Repository<UserSideEffectLog>, IUserSideEffectLogRepository
{
    public UserSideEffectLogRepository(AppDbContext db) : base(db) { }

    public async Task<(List<UserSideEffectLog> Items, int TotalCount)> GetByUserIdAsync(
        Guid userId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Set.AsNoTracking().Include(l => l.SideEffect).Where(l => l.UserId == userId);
        if (from.HasValue) query = query.Where(l => l.Date >= from.Value);
        if (to.HasValue) query = query.Where(l => l.Date <= to.Value);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(l => l.Date)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }
}

// ── ExternalMedication ──
public class ExternalMedicationRepository : Repository<ExternalMedication>, IExternalMedicationRepository
{
    public ExternalMedicationRepository(AppDbContext db) : base(db) { }

    public async Task<(List<ExternalMedication> Items, int TotalCount)> GetByUserIdAsync(
        Guid userId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Set.Where(e => e.UserId == userId);
        if (from.HasValue) query = query.Where(e => e.Date >= from.Value);
        if (to.HasValue) query = query.Where(e => e.Date <= to.Value);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(e => e.Date)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }
}

// ── MenstrualCycleEntry ──
public class MenstrualCycleRepository : Repository<MenstrualCycleEntry>, IMenstrualCycleRepository
{
    public MenstrualCycleRepository(AppDbContext db) : base(db) { }

    public async Task<(List<MenstrualCycleEntry> Items, int TotalCount)> GetByUserIdAsync(
        Guid userId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Set.Where(e => e.UserId == userId);
        if (from.HasValue) query = query.Where(e => e.StartDate >= from.Value);
        if (to.HasValue) query = query.Where(e => e.StartDate <= to.Value);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(e => e.StartDate)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public async Task<bool> HasOverlappingEntryAsync(Guid userId, DateTime startDate, DateTime? endDate, Guid? excludeId, CancellationToken ct = default)
    {
        // Два интервала перекрываются, если: NewStart <= ExistingEnd И NewEnd >= ExistingStart
        // Для nullable EndDate трактуем "без конца" как DateTime.MaxValue
        var newEnd = endDate ?? DateTime.MaxValue;

        var query = Set.Where(e => e.UserId == userId);
        if (excludeId.HasValue)
            query = query.Where(e => e.Id != excludeId.Value);

        return await query.AnyAsync(e =>
            startDate <= (e.EndDate ?? DateTime.MaxValue) && newEnd >= e.StartDate, ct);
    }
}

// ── RefreshToken ──
public class RefreshTokenRepository : Repository<RefreshToken>, IRefreshTokenRepository
{
    public RefreshTokenRepository(AppDbContext db) : base(db) { }

    public async Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken ct = default)
        => await Set.Include(rt => rt.User).FirstOrDefaultAsync(rt => rt.Token == token, ct);

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var tokens = await Set.Where(rt => rt.UserId == userId && !rt.IsRevoked).ToListAsync(ct);
        foreach (var t in tokens)
        {
            t.IsRevoked = true;
            t.RevokedAt = DateTime.UtcNow;
        }
    }
}

// ── ImportRecord ──
public class ImportRecordRepository : Repository<ImportRecord>, IImportRecordRepository
{
    public ImportRecordRepository(AppDbContext db) : base(db) { }

    public async Task<List<ImportRecord>> GetAllOrderedAsync(CancellationToken ct = default)
        => await Set.Include(r => r.ImportedBy).OrderByDescending(r => r.ImportedAt).ToListAsync(ct);
}