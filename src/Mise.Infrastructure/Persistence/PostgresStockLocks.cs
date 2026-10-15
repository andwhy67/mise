using Microsoft.EntityFrameworkCore;
using Mise.Application.Abstractions;
using Mise.Domain.Orders;
using Mise.Domain.Stock;

namespace Mise.Infrastructure.Persistence;

/// <summary>
/// Блокировки на PostgreSQL. Запросы параметризованы (FromSql с интерполяцией → параметры), не компонуются
/// дальше LINQ-ом — иначе EF обернул бы их в подзапрос, где FOR UPDATE недопустим. По той же причине глобальный
/// фильтр ресторана здесь отключён и заменён явным условием <c>restaurant_id = …</c> в самом SQL.
/// </summary>
internal sealed class PostgresStockLocks(MiseDbContext db, ITenant tenant) : IStockLocks
{
    private Guid Restaurant => tenant.RestaurantId ?? throw new InvalidOperationException("Блокировка данных ресторана без текущего ресторана.");

    public async Task<IReadOnlyList<Batch>> LockUsableBatchesAsync(IReadOnlyCollection<Guid> productIds, DateTimeOffset now, CancellationToken ct)
    {
        EnsureTransaction();
        if (productIds.Count == 0)
            return [];
        var ids = productIds.ToArray();
        var restaurant = Restaurant;
        return await db.Batches
            .FromSql($"""
                SELECT * FROM batches
                WHERE restaurant_id = {restaurant}
                  AND product_id = ANY({ids})
                  AND status = 'Active'
                  AND remaining_qty > 0
                  AND expires_at > {now}
                ORDER BY product_id, expires_at, id
                FOR UPDATE
                """)
            .IgnoreQueryFilters()
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Batch>> LockBatchesAsync(IReadOnlyCollection<Guid> batchIds, CancellationToken ct)
    {
        EnsureTransaction();
        if (batchIds.Count == 0)
            return [];
        var ids = batchIds.ToArray();
        var restaurant = Restaurant;
        return await db.Batches
            .FromSql($"""
                SELECT * FROM batches
                WHERE restaurant_id = {restaurant} AND id = ANY({ids})
                ORDER BY product_id, expires_at, id
                FOR UPDATE
                """)
            .IgnoreQueryFilters()
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Batch>> LockExpiryCandidatesAsync(DateTimeOffset now, bool includeExpired, int limit, CancellationToken ct)
    {
        EnsureTransaction();
        var restaurant = Restaurant;
        return await db.Batches
            .FromSql($"""
                SELECT * FROM batches
                WHERE restaurant_id = {restaurant}
                  AND remaining_qty > 0
                  AND ((status = 'Active' AND expires_at <= {now}) OR ({includeExpired} AND status = 'Expired'))
                ORDER BY product_id, expires_at, id
                LIMIT {limit}
                FOR UPDATE
                """)
            .IgnoreQueryFilters()
            .ToListAsync(ct);
    }

    public async Task<Order?> LockOrderAsync(Guid orderId, CancellationToken ct)
    {
        EnsureTransaction();
        var restaurant = Restaurant;
        return (await db.Orders.FromSql($"SELECT * FROM orders WHERE restaurant_id = {restaurant} AND id = {orderId} FOR UPDATE")
            .IgnoreQueryFilters()
            .ToListAsync(ct)).SingleOrDefault();
    }

    public async Task AcquireTransactionLockAsync(string name, CancellationToken ct)
    {
        EnsureTransaction();
        var key = Scoped(name);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({key}))", ct);
    }

    public async Task<bool> TryAcquireTransactionLockAsync(string name, CancellationToken ct)
    {
        EnsureTransaction();
        var key = Scoped(name);
        return await db.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtext({key})) AS \"Value\"")
            .SingleAsync(ct);
    }

    /// <summary>
    /// Номер заказа — свой счётчик у каждого ресторана: UPDATE … RETURNING держит блокировку строки ресторана
    /// до конца транзакции заказа, параллельные заказы получают номера по очереди, без пропусков при откате.
    /// </summary>
    public async Task<long> NextOrderNumberAsync(CancellationToken ct)
    {
        EnsureTransaction();
        var restaurant = Restaurant;
        return (await db.Database
            .SqlQuery<long>($"UPDATE restaurants SET last_order_number = last_order_number + 1 WHERE id = {restaurant} RETURNING last_order_number AS \"Value\"")
            // Без LINQ-операторов: EF не оборачивает UPDATE … RETURNING в подзапрос (Postgres такого не принимает).
            .ToListAsync(ct)).Single();
    }

    /// <summary>Advisory-лок — в пределах ресторана: стоп-лист соседа не ждёт нашего пересчёта. Без ресторана — общий.</summary>
    private string Scoped(string name) => tenant.RestaurantId is { } id ? $"{name}:{id:N}" : name;

    private void EnsureTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Блокировка строк требует открытой транзакции.");
    }
}
