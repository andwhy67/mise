using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mise.Domain.Alerts;
using Mise.Domain.Catalog;
using Mise.Domain.Stock;

namespace Mise.Infrastructure.Persistence.Configurations;

internal sealed class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.ToTable("alerts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.Severity).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.Message).HasMaxLength(500).IsRequired();
        builder.Property(a => a.DedupKey).HasMaxLength(100);
        builder.Ignore(a => a.IsActive);

        // Страховка от дублей на уровне БД: один открытый алерт на ключ (партия+уровень, LowStock продукта).
        builder.HasIndex(a => a.DedupKey).IsUnique()
            .HasFilter("dedup_key IS NOT NULL AND resolved_at IS NULL")
            .HasDatabaseName("ux_alerts_open_dedup_key");
        builder.HasIndex(a => a.CreatedAt);
        builder.HasIndex(a => a.BatchId);
        builder.HasIndex(a => a.ProductId);
        builder.HasIndex(a => a.DishId);
        // Счётчик «требуют внимания».
        builder.HasIndex(a => a.Severity).HasFilter("acknowledged_at IS NULL AND resolved_at IS NULL")
            .HasDatabaseName("ix_alerts_attention");

        builder.HasOne<Batch>().WithMany().HasForeignKey(a => a.BatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Dish>().WithMany().HasForeignKey(a => a.DishId).OnDelete(DeleteBehavior.Cascade);
    }
}
