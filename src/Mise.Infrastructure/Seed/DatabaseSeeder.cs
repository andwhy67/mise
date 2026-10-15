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

        // База, засеянная до появления цен: заполняем цену и поставщика, только если их ещё нет (правки менеджера не трогаем).
        var unpriced = await db.Products.Where(p => existingProducts.Contains(p.Id) && p.ReferenceUnitCost == null).ToListAsync(ct);
        foreach (var product in unpriced)
        {
            var seed = products.First(p => p.Id == product.Id);
            product.ReferenceUnitCost = seed.ReferenceUnitCost;
            product.Supplier ??= seed.Supplier;
        }

        // Замены — если в базе их ещё нет совсем (база, засеянная до них); заданные менеджером не трогаем.
        var withoutRules = await db.Products.AnyAsync(p => p.Substitutes.Any(), ct)
            ? []
            : await db.Products.Include(p => p.Substitutes).Where(p => existingProducts.Contains(p.Id)).ToListAsync(ct);
        foreach (var product in withoutRules)
            product.SetSubstitutes(SeedData.Substitutes.Where(s => s.ProductId == product.Id && existingProducts.Contains(s.SubstituteId))
                .Select(s => (s.SubstituteId, s.Ratio)));
        var rulesAdded = withoutRules.Any(p => p.Substitutes.Count > 0);

        var dishes = SeedData.CreateDishes();
        var dishIds = dishes.Select(d => d.Id).ToList();
        var existingDishes = await db.Dishes.Where(d => dishIds.Contains(d.Id)).Select(d => d.Id).ToListAsync(ct);
        var newDishes = dishes.Where(d => !existingDishes.Contains(d.Id)).ToList();
        db.Dishes.AddRange(newDishes);

        var optional = SeedData.Optional.ToList();
        // База, засеянная до флага «можно убрать»: если ни у одного ингредиента демо-блюд его нет — ставим по стартовому словарю.
        var unflagged = await db.DishIngredients.AnyAsync(i => dishIds.Contains(i.DishId) && i.Removable, ct)
            ? []
            : await db.DishIngredients.Where(i => dishIds.Contains(i.DishId) && optional.Contains(i.ProductId)).ToListAsync(ct);
        foreach (var ingredient in unflagged)
            db.Entry(ingredient).Property(i => i.Removable).CurrentValue = true;

        if (newProducts.Count + newDishes.Count + unpriced.Count + unflagged.Count == 0 && !rulesAdded)
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
