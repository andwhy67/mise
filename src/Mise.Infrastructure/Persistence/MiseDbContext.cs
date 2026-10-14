using Microsoft.EntityFrameworkCore;
using Mise.Application.Abstractions;
using Mise.Application.Common;
using Mise.Domain.Alerts;
using Mise.Domain.Audit;
using Mise.Domain.Push;
using Mise.Domain.Users;
using Mise.Domain.Catalog;
using Mise.Domain.Menu;
using Mise.Domain.Orders;
using Mise.Domain.Stock;
using Mise.Domain.Supplies;
using Mise.Infrastructure.Persistence.Configurations;
using Npgsql;

namespace Mise.Infrastructure.Persistence;

public sealed class MiseDbContext(DbContextOptions<MiseDbContext> options) : DbContext(options), IMiseDbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Dish> Dishes => Set<Dish>();
    public DbSet<DishIngredient> DishIngredients => Set<DishIngredient>();
    public DbSet<Supply> Supplies => Set<Supply>();
    public DbSet<SupplyItem> SupplyItems => Set<SupplyItem>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<StopListEntry> StopListEntries => Set<StopListEntry>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<ConsumptionHour> ConsumptionHours => Set<ConsumptionHour>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<OrderShortage> OrderShortages => Set<OrderShortage>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            throw new UniqueConstraintViolationException(pg.ConstraintName, ex);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>(OrderConfiguration.NumberSequence).StartsAt(1).IncrementsBy(1);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MiseDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Количества: numeric(14,3) по умолчанию для всех decimal (PLAN §2.2); деньги переопределяются явно.
        configurationBuilder.Properties<decimal>().HavePrecision(14, 3);
    }
}
