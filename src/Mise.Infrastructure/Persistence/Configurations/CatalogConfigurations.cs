using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mise.Domain.Catalog;

namespace Mise.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        // Цена за грамм бывает 0,037 ₽ — нужна точность 6 знаков (по умолчанию в проекте 14,3).
        builder.Property(p => p.ReferenceUnitCost).HasPrecision(18, 6);
        builder.Property(p => p.Supplier).HasMaxLength(200);
        builder.ToTable("products");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Unit).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.Category).HasMaxLength(100);
        builder.Property(p => p.StorageType).HasMaxLength(100);
        // Название уникально внутри ресторана: у соседнего ресторана тоже может быть «Шпинат».
        builder.HasIndex(MiseDbContext.RestaurantIdProperty, nameof(Product.Name)).IsUnique();
        builder.HasMany(p => p.Substitutes).WithOne().HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Substitutes).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Property(p => p.PrepYield).HasPrecision(14, 3);
        builder.HasMany(p => p.PrepIngredients).WithOne().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.PrepIngredients).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_products_default_shelf_life_positive", "default_shelf_life_hours IS NULL OR default_shelf_life_hours > 0");
            t.HasCheckConstraint("ck_products_min_stock_non_negative", "min_stock_qty >= 0");
            t.HasCheckConstraint("ck_products_pack_size_positive", "pack_size > 0");
        });
    }
}

internal sealed class ProductSubstituteConfiguration : IEntityTypeConfiguration<ProductSubstitute>
{
    public void Configure(EntityTypeBuilder<ProductSubstitute> builder)
    {
        builder.ToTable("product_substitutes", t =>
        {
            t.HasCheckConstraint("ck_product_substitutes_ratio_positive", "ratio > 0");
            t.HasCheckConstraint("ck_product_substitutes_not_self", "product_id <> substitute_id");
        });
        builder.HasKey(s => new { s.ProductId, s.SubstituteId });
        builder.Property(s => s.Ratio).HasPrecision(8, 4);
        // Удалили заменитель — правило замены теряет смысл и уходит вместе с ним.
        builder.HasOne<Product>().WithMany().HasForeignKey(s => s.SubstituteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => s.SubstituteId);
    }
}

internal sealed class PrepIngredientConfiguration : IEntityTypeConfiguration<PrepIngredient>
{
    public void Configure(EntityTypeBuilder<PrepIngredient> builder)
    {
        builder.ToTable("product_prep_ingredients", t =>
        {
            t.HasCheckConstraint("ck_product_prep_ingredients_quantity_positive", "quantity > 0");
            t.HasCheckConstraint("ck_product_prep_ingredients_not_self", "product_id <> ingredient_id");
        });
        builder.HasKey(i => new { i.ProductId, i.IngredientId });
        // Продукт из рецепта заготовки удалить нельзя — как и продукт из состава блюда.
        builder.HasOne<Product>().WithMany().HasForeignKey(i => i.IngredientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => i.IngredientId);
    }
}

internal sealed class DishConfiguration : IEntityTypeConfiguration<Dish>
{
    public void Configure(EntityTypeBuilder<Dish> builder)
    {
        builder.ToTable("dishes");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Name).HasMaxLength(200).IsRequired();
        builder.Property(d => d.Category).HasMaxLength(100);
        builder.Property(d => d.Price).HasPrecision(12, 2);
        builder.HasIndex(MiseDbContext.RestaurantIdProperty, nameof(Dish.Name)).IsUnique();
        builder.HasMany(d => d.Ingredients).WithOne().HasForeignKey(i => i.DishId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(d => d.Ingredients).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class DishIngredientConfiguration : IEntityTypeConfiguration<DishIngredient>
{
    public void Configure(EntityTypeBuilder<DishIngredient> builder)
    {
        builder.ToTable("dish_ingredients", t => t.HasCheckConstraint("ck_dish_ingredients_quantity_positive", "quantity > 0"));
        builder.HasKey(i => new { i.DishId, i.ProductId });
        builder.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => i.ProductId);
    }
}
