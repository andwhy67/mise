using Mise.Application.Abstractions;
using Mise.Application.Restaurants;
using Mise.Domain.Catalog;

namespace Mise.Infrastructure.Seed;

/// <summary>
/// Стартовый справочник для нового ресторана — копия демо-словаря со своими id: ресторан правит его как хочет,
/// демо-ресторан и соседи этого не видят. Замены и составы блюд переводятся на новые id продуктов.
/// </summary>
internal sealed class DemoCatalogTemplate : ICatalogTemplate
{
    public void AddTo(IMiseDbContext db, DateTimeOffset now)
    {
        var products = SeedData.CreateProducts();
        var ids = products.ToDictionary(p => p.Id, _ => Guid.CreateVersion7(now));

        foreach (var source in products)
        {
            var copy = new Product(ids[source.Id], source.Name, source.Unit)
            {
                DefaultShelfLifeHours = source.DefaultShelfLifeHours,
                MinStockQty = source.MinStockQty,
                PackSize = source.PackSize,
                Category = source.Category,
                StorageType = source.StorageType,
                ReferenceUnitCost = source.ReferenceUnitCost,
                Supplier = source.Supplier,
            };
            copy.SetSubstitutes(source.Substitutes.Select(s => (ids[s.SubstituteId], s.Ratio)));
            if (source.PrepYield is { } yield)
                copy.SetPrepRecipe(yield, source.PrepIngredients.Select(i => (ids[i.IngredientId], i.Quantity)));
            db.Products.Add(copy);
        }

        foreach (var source in SeedData.CreateDishes())
        {
            var copy = new Dish(Guid.CreateVersion7(now), source.Name)
            {
                Category = source.Category,
                Price = source.Price,
                IsActive = source.IsActive,
            };
            copy.SetIngredients(source.Ingredients.Select(i => (ids[i.ProductId], i.Quantity, i.Removable)));
            db.Dishes.Add(copy);
        }
    }
}
