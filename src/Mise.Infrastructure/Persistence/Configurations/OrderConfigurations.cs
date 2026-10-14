using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mise.Domain.Catalog;
using Mise.Domain.Menu;
using Mise.Domain.Orders;
using Mise.Domain.Stock;

namespace Mise.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public const string NumberSequence = "order_number_seq";

    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Number).HasMaxLength(32).IsRequired();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(o => o.IdempotencyKey).HasMaxLength(200);
        builder.Property(o => o.RequestHash).HasMaxLength(64);
        builder.Ignore(o => o.Total);
        builder.Ignore(o => o.CanBeCancelled);
        builder.HasIndex(o => o.Number).IsUnique();
        builder.HasIndex(o => o.IdempotencyKey).IsUnique().HasFilter("idempotency_key IS NOT NULL");
        builder.HasIndex(o => new { o.Status, o.CreatedAt });
        builder.HasIndex(o => o.CreatedAt);
        builder.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items", t =>
        {
            t.HasCheckConstraint("ck_order_items_quantity_positive", "quantity > 0");
            t.HasCheckConstraint("ck_order_items_price_non_negative", "price_snapshot >= 0");
        });
        builder.HasKey(i => new { i.OrderId, i.LineNo });
        builder.Property(i => i.DishName).HasMaxLength(200).IsRequired();
        builder.Property(i => i.PriceSnapshot).HasPrecision(12, 2);
        builder.HasIndex(i => i.DishId);
        builder.HasOne<Dish>().WithMany().HasForeignKey(i => i.DishId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StopListEntryConfiguration : IEntityTypeConfiguration<StopListEntry>
{
    public void Configure(EntityTypeBuilder<StopListEntry> builder)
    {
        builder.ToTable("stoplist_history", t => t.HasCheckConstraint("ck_stoplist_history_interval", "ended_at IS NULL OR ended_at >= started_at"));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Reason).HasConversion<string>().HasMaxLength(16);
        builder.Ignore(e => e.IsOpen);
        // Не больше одной открытой записи на блюдо.
        builder.HasIndex(e => e.DishId).IsUnique().HasFilter("ended_at IS NULL").HasDatabaseName("ux_stoplist_history_open_dish");
        builder.HasIndex(e => new { e.DishId, e.StartedAt });
        builder.HasIndex(e => e.LimitingProductId);
        builder.HasOne<Dish>().WithMany().HasForeignKey(e => e.DishId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Product>().WithMany().HasForeignKey(e => e.LimitingProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Связь движений с заказами (колонка order_id добавлена в фазе 2).</summary>
internal sealed class StockMovementOrderConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.HasIndex(m => m.OrderId);
        builder.HasOne<Order>().WithMany().HasForeignKey(m => m.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OrderShortageConfiguration : IEntityTypeConfiguration<OrderShortage>
{
    public void Configure(EntityTypeBuilder<OrderShortage> builder)
    {
        builder.ToTable("order_shortages");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Required).HasPrecision(14, 3);
        builder.Property(s => s.Available).HasPrecision(14, 3);
        builder.Property(s => s.Shortage).HasPrecision(14, 3);
        // Отчёт берёт отказы за период по продукту.
        builder.HasIndex(s => new { s.ProductId, s.CreatedAt });
        builder.HasIndex(s => s.CreatedAt);
        builder.HasOne<Product>().WithMany().HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}
