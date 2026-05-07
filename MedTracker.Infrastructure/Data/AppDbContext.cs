using MedTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedTracker.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Diagnosis> Diagnoses => Set<Diagnosis>();
    public DbSet<UserDiagnosis> UserDiagnoses => Set<UserDiagnosis>();
    public DbSet<Medication> Medications => Set<Medication>();
    public DbSet<Supplement> Supplements => Set<Supplement>();
    public DbSet<SideEffect> SideEffects => Set<SideEffect>();
    public DbSet<UserMedication> UserMedications => Set<UserMedication>();
    public DbSet<UserSupplement> UserSupplements => Set<UserSupplement>();
    public DbSet<UserSideEffectLog> UserSideEffectLogs => Set<UserSideEffectLog>();
    public DbSet<ExternalMedication> ExternalMedications => Set<ExternalMedication>();
    public DbSet<MenstrualCycleEntry> MenstrualCycleEntries => Set<MenstrualCycleEntry>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ImportRecord> ImportRecords => Set<ImportRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        modelBuilder.Entity<UserMedication>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<UserSupplement>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<UserSideEffectLog>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<ExternalMedication>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<MenstrualCycleEntry>().HasQueryFilter(e => !e.IsDeleted);

        modelBuilder.Entity<Diagnosis>().HasData(
            new Diagnosis { Id = Guid.Parse("10000000-0000-0000-0000-000000000001"), Name = "ПМР" },
            new Diagnosis { Id = Guid.Parse("10000000-0000-0000-0000-000000000002"), Name = "СПКЯ" },
            new Diagnosis { Id = Guid.Parse("10000000-0000-0000-0000-000000000003"), Name = "Эндометриоз" },
            new Diagnosis { Id = Guid.Parse("10000000-0000-0000-0000-000000000004"), Name = "Менопауза" }
        );
    }
}