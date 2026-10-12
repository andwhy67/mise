using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mise.Application.Common;
using Mise.Infrastructure.Persistence;

namespace Mise.Infrastructure.Seed;

/// <summary>Идемпотентно добавляет недостающие продукты и блюда из <see cref="SeedData"/>; существующие не трогает.</summary>
public sealed class DatabaseSeeder(MiseDbContext db, ILogger<DatabaseSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var products = SeedData.CreateProducts();
        var productIds = products.Select(p => p.Id).ToList();
        var existingProducts = await db.Products.Where(p => productIds.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
        var newProducts = products.Where(p => !existingProducts.Contains(p.Id)).ToList();
        db.Products.AddRange(newProducts);

        var dishes = SeedData.CreateDishes();
        var dishIds = dishes.Select(d => d.Id).ToList();
        var existingDishes = await db.Dishes.Where(d => dishIds.Contains(d.Id)).Select(d => d.Id).ToListAsync(ct);
        var newDishes = dishes.Where(d => !existingDishes.Contains(d.Id)).ToList();
        db.Dishes.AddRange(newDishes);

        if (newProducts.Count + newDishes.Count == 0)
            return;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException ex)
        {
            // Например, продукт с таким названием уже создан вручную или сидер параллельно запущен другим инстансом.
            logger.LogWarning(ex, "Seed skipped: {Constraint}", ex.ConstraintName);
            db.ChangeTracker.Clear();
            return;
        }

        logger.LogInformation("Seed: added {Products} products and {Dishes} dishes", newProducts.Count, newDishes.Count);
    }
}
