using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Mise.Domain.Alerts;
using Mise.Domain.Catalog;
using Mise.Domain.Menu;
using Mise.Domain.Orders;
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
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<StopListEntry> StopListEntries { get; }
    DbSet<Alert> Alerts { get; }
    DbSet<ConsumptionHour> ConsumptionHours { get; }

    ChangeTracker ChangeTracker { get; }

    /// <summary>Транзакции (BeginTransactionAsync) для операций, меняющих остатки.</summary>
    DatabaseFacade Database { get; }

    /// <summary>Сохраняет изменения. Нарушение уникальности → <see cref="Common.UniqueConstraintViolationException"/>.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
