using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mise.Domain.Audit;
using Mise.Domain.Users;

namespace Mise.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.Login).HasMaxLength(User.LoginMaxLength).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(u => u.Login).IsUnique();
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(t => t.RevokeReason).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.FamilyId);
        builder.HasIndex(t => new { t.UserId, t.RevokedAt });
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_log");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Action).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Login).HasMaxLength(64);
        builder.Property(a => a.Entity).HasMaxLength(64);
        builder.Property(a => a.EntityId).HasMaxLength(64);
        builder.Property(a => a.Details).HasColumnType("jsonb");
        builder.Property(a => a.Ip).HasMaxLength(64);
        builder.HasIndex(a => a.CreatedAt);
        builder.HasIndex(a => new { a.Action, a.CreatedAt });
        builder.HasIndex(a => a.UserId);
        // Без FK на users: журнал переживает удаление пользователя и хранит попытки входа под несуществующим логином.
    }
}
