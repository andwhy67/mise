using System.Linq.Expressions;
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
using Mise.Domain.Restaurants;
using Mise.Domain.Stock;
using Mise.Domain.Supplies;
using Mise.Infrastructure.Persistence.Configurations;
using Npgsql;

namespace Mise.Infrastructure.Persistence;

public sealed class MiseDbContext(DbContextOptions<MiseDbContext> options, ITenant tenant) : DbContext(options), IMiseDbContext
{
    /// <summary>Теневое свойство «чей ресторан» у корневых сущностей; дочерние (состав, строки) — через родителя.</summary>
    public const string RestaurantIdProperty = "RestaurantId";

    /// <summary>
    /// Корневые сущности ресторана: глобальный фильтр по текущему ресторану и автоматическое проставление при сохранении.
    /// Id продуктов, партий и заказов — глобально уникальные GUID, поэтому дочерним таблицам отдельный столбец не нужен.
    /// </summary>
    public static readonly Type[] RestaurantOwned =
    [
        typeof(Product), typeof(Dish), typeof(Supply), typeof(Batch), typeof(StockMovement), typeof(Order),
        typeof(StopListEntry), typeof(Alert), typeof(ConsumptionHour), typeof(User), typeof(PushSubscription),
        typeof(OrderShortage), typeof(AuditEntry), typeof(Invite),
    ];

    /// <summary>Журнал может быть «ничьим»: неудачный вход с неизвестным логином ещё не знает ресторана.</summary>
    private static readonly Type[] OptionalOwner = [typeof(AuditEntry)];

    /// <summary>Нет ресторана — фильтр по пустому id: запрос вернёт ничего, а не всё.</summary>
    public Guid CurrentRestaurantId => tenant.RestaurantId ?? Guid.Empty;

    public DbSet<Restaurant> Restaurants => Set<Restaurant>();
    public DbSet<Invite> Invites => Set<Invite>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Dish> Dishes => Set<Dish>();
    public DbSet<DishIngredient> DishIngredients => Set<DishIngredient>();
    public DbSet<PrepIngredient> PrepIngredients => Set<PrepIngredient>();
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
        StampRestaurant();
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            throw new UniqueConstraintViolationException(pg.ConstraintName, ex);
        }
    }

    /// <summary>Новая запись ресторана получает текущий ресторан; без него — ошибка, а не «ничья» запись.</summary>
    private void StampRestaurant()
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Added))
        {
            var property = entry.Metadata.FindProperty(RestaurantIdProperty);
            if (property is null)
                continue;
            var current = entry.Property(RestaurantIdProperty);
            if (current.CurrentValue is Guid id && id != Guid.Empty)
                continue;
            if (tenant.RestaurantId is { } restaurantId)
                current.CurrentValue = restaurantId;
            else if (!property.IsNullable)
                throw new InvalidOperationException($"Нельзя сохранить {entry.Metadata.ClrType.Name} без ресторана: текущий ресторан не задан.");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Сначала теневой RestaurantId — конфигурации ниже строят на нём индексы «уникально внутри ресторана».
        foreach (var type in RestaurantOwned)
        {
            var optional = OptionalOwner.Contains(type);
            var entity = modelBuilder.Entity(type);
            entity.Property(optional ? typeof(Guid?) : typeof(Guid), RestaurantIdProperty);
            entity.HasOne(typeof(Restaurant)).WithMany().HasForeignKey(RestaurantIdProperty).OnDelete(DeleteBehavior.Restrict)
                .IsRequired(!optional);
            entity.HasIndex(RestaurantIdProperty);
            entity.HasQueryFilter(RestaurantFilter(type, optional));
        }

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MiseDbContext).Assembly);
    }

    /// <summary><c>e =&gt; EF.Property&lt;Guid&gt;(e, "RestaurantId") == this.CurrentRestaurantId</c> — значение берётся при каждом запросе.</summary>
    private LambdaExpression RestaurantFilter(Type type, bool optional)
    {
        var e = Expression.Parameter(type, "e");
        var propertyType = optional ? typeof(Guid?) : typeof(Guid);
        var property = Expression.Call(typeof(EF), nameof(EF.Property), [propertyType], e, Expression.Constant(RestaurantIdProperty));
        Expression current = Expression.Property(Expression.Constant(this), nameof(CurrentRestaurantId));
        if (optional)
            current = Expression.Convert(current, typeof(Guid?));
        return Expression.Lambda(Expression.Equal(property, current), e);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Количества: numeric(14,3) по умолчанию для всех decimal (PLAN §2.2); деньги переопределяются явно.
        configurationBuilder.Properties<decimal>().HavePrecision(14, 3);
    }
}
