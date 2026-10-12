using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Mise.Domain.Catalog;
using Mise.Domain.Stock;
using Mise.Domain.Supplies;

namespace Mise.Application.Abstractions;

/// <summary>
/// Доступ use cases к данным. Реализуется EF DbContext в Infrastructure; отдельных репозиториев не заводим.
/// </summary>
public interface IMiseDbContext
{
    DbSet<Product> Products { get; }
    DbSet<Dish> Dishes { get; }
    DbSet<DishIngredient> DishIngredients { get; }
    DbSet<Supply> Supplies { get; }
    DbSet<SupplyItem> SupplyItems { get; }
    DbSet<Batch> Batches { get; }
    DbSet<StockMovement> StockMovements { get; }

    ChangeTracker ChangeTracker { get; }

    /// <summary>Сохраняет изменения. Нарушение уникальности → <see cref="Common.UniqueConstraintViolationException"/>.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
