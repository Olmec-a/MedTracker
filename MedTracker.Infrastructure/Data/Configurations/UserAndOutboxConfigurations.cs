using MedTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedTracker.Infrastructure.Data.Configurations;

/// <summary>
/// ВАЖНО: эта версия ЗАМЕНЯЕТ старый UserConfiguration в EntityConfigurations.cs.
/// Удали старый класс UserConfiguration из EntityConfigurations.cs, оставь только эту версию.
/// </summary>
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(e => e.Id);

        // Email — новый identity-field. Длина 254 — RFC 5321 SMTP-лимит на полный адрес.
        builder.Property(e => e.Email)
            .HasMaxLength(254)
            .IsRequired();
        builder.HasIndex(e => e.Email).IsUnique();

        builder.Property(e => e.PasswordHash).IsRequired();
        builder.Property(e => e.FullName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.FailedLoginAttempts).HasDefaultValue(0);

        // Email confirmation
        builder.Property(e => e.EmailConfirmed).HasDefaultValue(false);
        builder.Property(e => e.EmailConfirmationTokenHash).HasMaxLength(128); // SHA-256 hex = 64, запас
        builder.Property(e => e.PasswordResetTokenHash).HasMaxLength(128);
    }
}

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.MessageType).HasMaxLength(50).IsRequired();
        builder.Property(e => e.ToAddress).HasMaxLength(254).IsRequired();
        builder.Property(e => e.Subject).HasMaxLength(500).IsRequired();
        builder.Property(e => e.BodyHtml).IsRequired();
        builder.Property(e => e.LastError).HasMaxLength(2000);

        // Индекс для poll-запроса воркера: ProcessedAt IS NULL ORDER BY CreatedAt
        builder.HasIndex(e => new { e.ProcessedAt, e.NextRetryAt })
            .HasDatabaseName("IX_OutboxMessages_PollOrder");
    }
}