using MedTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedTracker.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.Login).IsUnique();
        builder.Property(e => e.Login).HasMaxLength(50).IsRequired();
        builder.Property(e => e.PasswordHash).IsRequired();
        builder.Property(e => e.FullName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.FailedLoginAttempts).HasDefaultValue(0);
    }
}

public class DiagnosisConfiguration : IEntityTypeConfiguration<Diagnosis>
{
    public void Configure(EntityTypeBuilder<Diagnosis> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.Name).IsUnique();
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
    }
}

public class UserDiagnosisConfiguration : IEntityTypeConfiguration<UserDiagnosis>
{
    public void Configure(EntityTypeBuilder<UserDiagnosis> builder)
    {
        builder.HasKey(e => new { e.UserId, e.DiagnosisId });

        builder.HasOne(e => e.User)
            .WithMany(u => u.UserDiagnoses)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Diagnosis)
            .WithMany(d => d.UserDiagnoses)
            .HasForeignKey(e => e.DiagnosisId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.DiagnosisId);
    }
}

public class MedicationConfiguration : IEntityTypeConfiguration<Medication>
{
    public void Configure(EntityTypeBuilder<Medication> builder)
    {
        builder.HasKey(e => e.Id);
        // No length limits on text fields — справочные данные могут быть произвольной длины
        builder.Property(e => e.HormonalGroup);
        builder.Property(e => e.INN);
        builder.Property(e => e.TradeName);
        builder.Property(e => e.Dosage);
        builder.Property(e => e.Form);
        builder.Property(e => e.Frequency);
        builder.Property(e => e.Diet);

        builder.HasOne(e => e.Diagnosis)
            .WithMany(d => d.Medications)
            .HasForeignKey(e => e.DiagnosisId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.DiagnosisId);
    }
}

public class SupplementConfiguration : IEntityTypeConfiguration<Supplement>
{
    public void Configure(EntityTypeBuilder<Supplement> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired();
        builder.Property(e => e.Dosage);
        builder.Property(e => e.Frequency);

        builder.HasOne(e => e.Medication)
            .WithMany(m => m.Supplements)
            .HasForeignKey(e => e.MedicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.MedicationId);
    }
}

public class SideEffectConfiguration : IEntityTypeConfiguration<SideEffect>
{
    public void Configure(EntityTypeBuilder<SideEffect> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired();

        builder.HasOne(e => e.Medication)
            .WithMany(m => m.SideEffects)
            .HasForeignKey(e => e.MedicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.MedicationId);
    }
}

public class UserMedicationConfiguration : IEntityTypeConfiguration<UserMedication>
{
    public void Configure(EntityTypeBuilder<UserMedication> builder)
    {
        builder.HasKey(e => e.Id);

        builder.HasOne(e => e.User)
            .WithMany(u => u.UserMedications)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Medication)
            .WithMany(m => m.UserMedications)
            .HasForeignKey(e => e.MedicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.MedicationId);
        builder.HasIndex(e => e.IsActive);
    }
}

public class UserSupplementConfiguration : IEntityTypeConfiguration<UserSupplement>
{
    public void Configure(EntityTypeBuilder<UserSupplement> builder)
    {
        builder.HasKey(e => e.Id);

        builder.HasOne(e => e.User)
            .WithMany(u => u.UserSupplements)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Supplement)
            .WithMany(s => s.UserSupplements)
            .HasForeignKey(e => e.SupplementId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.SupplementId);
    }
}

public class UserSideEffectLogConfiguration : IEntityTypeConfiguration<UserSideEffectLog>
{
    public void Configure(EntityTypeBuilder<UserSideEffectLog> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Intensity).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Comment).HasMaxLength(1000);

        builder.HasOne(e => e.User)
            .WithMany(u => u.SideEffectLogs)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.SideEffect)
            .WithMany(se => se.UserSideEffectLogs)
            .HasForeignKey(e => e.SideEffectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.Date);
    }
}

public class ExternalMedicationConfiguration : IEntityTypeConfiguration<ExternalMedication>
{
    public void Configure(EntityTypeBuilder<ExternalMedication> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(300).IsRequired();
        builder.Property(e => e.Dosage).HasMaxLength(100);
        builder.Property(e => e.Comment).HasMaxLength(1000);

        builder.HasOne(e => e.User)
            .WithMany(u => u.ExternalMedications)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.Date);
    }
}

public class MenstrualCycleEntryConfiguration : IEntityTypeConfiguration<MenstrualCycleEntry>
{
    public void Configure(EntityTypeBuilder<MenstrualCycleEntry> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Intensity).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Notes).HasMaxLength(2000);

        // Store symptoms as jsonb
        builder.Property(e => e.Symptoms)
            .HasColumnType("jsonb");

        builder.HasOne(e => e.User)
            .WithMany(u => u.MenstrualCycleEntries)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.StartDate);
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.Token).IsUnique();
        builder.Property(e => e.Token).HasMaxLength(500).IsRequired();

        builder.HasOne(e => e.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.UserId);
    }
}

public class ImportRecordConfiguration : IEntityTypeConfiguration<ImportRecord>
{
    public void Configure(EntityTypeBuilder<ImportRecord> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.FileName).HasMaxLength(500).IsRequired();
        builder.Property(e => e.DiagnosisName).HasMaxLength(100).IsRequired();

        builder.HasOne(e => e.ImportedBy)
            .WithMany()
            .HasForeignKey(e => e.ImportedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}