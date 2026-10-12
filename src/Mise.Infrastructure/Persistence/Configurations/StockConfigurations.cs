using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mise.Domain.Catalog;
using Mise.Domain.Stock;
using Mise.Domain.Supplies;

namespace Mise.Infrastructure.Persistence.Configurations;

internal sealed class SupplyConfiguration : IEntityTypeConfiguration<Supply>
{
    public void Configure(EntityTypeBuilder<Supply> builder)
    {
        builder.ToTable("supplies");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Number).HasMaxLength(64).IsRequired();
        builder.Property(s => s.SupplierName).HasMaxLength(200);
        builder.Property(s => s.IdempotencyKey).HasMaxLength(200);
        builder.Property(s => s.RequestHash).HasMaxLength(64);
        builder.HasIndex(s => s.Number).IsUnique();
        builder.HasIndex(s => s.IdempotencyKey).IsUnique().HasFilter("idempotency_key IS NOT NULL");
        builder.HasIndex(s => s.CreatedAt);
        builder.HasMany(s => s.Items).WithOne().HasForeignKey(i => i.SupplyId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(s => s.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class SupplyItemConfiguration : IEntityTypeConfiguration<SupplyItem>
{
    public void Configure(EntityTypeBuilder<SupplyItem> builder)
    {
        builder.ToTable("supply_items", t =>
        {
            t.HasCheckConstraint("ck_supply_items_quantity_positive", "quantity > 0");
            t.HasCheckConstraint("ck_supply_items_shelf_life_positive", "shelf_life_hours IS NULL OR shelf_life_hours > 0");
        });
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.ProductName).HasMaxLength(200).IsRequired();
        builder.HasIndex(i => new { i.SupplyId, i.LineNo }).IsUnique();
        builder.HasIndex(i => i.ProductId);
        builder.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> builder)
    {
        builder.ToTable("batches", t =>
        {
            // Остаток не может быть отрицательным никогда (CLAUDE.md, правило 5) — страхуемся и на уровне БД.
            t.HasCheckConstraint("ck_batches_remaining_non_negative", "remaining_qty >= 0");
            t.HasCheckConstraint("ck_batches_remaining_le_initial", "remaining_qty <= initial_qty");
            t.HasCheckConstraint("ck_batches_initial_positive", "initial_qty > 0");
        });
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(b => b.ExpirySource).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(b => b.SupplyItemId).IsUnique();
        // FEFO: партии продукта по статусу в порядке срока.
        builder.HasIndex(b => new { b.ProductId, b.Status, b.ExpiresAt });
        // Джоб просрочки: только активные.
        builder.HasIndex(b => b.ExpiresAt).HasFilter("status = 'Active'").HasDatabaseName("ix_batches_expires_at_active");
        builder.HasOne<SupplyItem>().WithMany().HasForeignKey(b => b.SupplyItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(b => b.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements", t => t.HasCheckConstraint("ck_stock_movements_delta_non_zero", "qty_delta <> 0"));
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(m => m.Reason).HasMaxLength(500);
        builder.HasIndex(m => m.CreatedAt);
        builder.HasIndex(m => new { m.ProductId, m.CreatedAt });
        builder.HasIndex(m => m.BatchId);
        builder.HasOne<Batch>().WithMany().HasForeignKey(m => m.BatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Supply>().WithMany().HasForeignKey(m => m.SupplyId).OnDelete(DeleteBehavior.Restrict);
    }
}
