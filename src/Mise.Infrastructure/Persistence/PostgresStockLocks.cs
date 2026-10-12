using Microsoft.EntityFrameworkCore;
using Mise.Application.Abstractions;
using Mise.Domain.Orders;
using Mise.Domain.Stock;
using Mise.Infrastructure.Persistence.Configurations;

namespace Mise.Infrastructure.Persistence;

/// <summary>
/// Блокировки на PostgreSQL. Запросы параметризованы (FromSql с интерполяцией → параметры), не компонуются
/// дальше LINQ-ом — иначе EF обернул бы их в подзапрос, где FOR UPDATE недопустим.
/// </summary>
internal sealed class PostgresStockLocks(MiseDbContext db) : IStockLocks
{
    public async Task<IReadOnlyList<Batch>> LockUsableBatchesAsync(IReadOnlyCollection<Guid> productIds, DateTimeOffset now, CancellationToken ct)
    {
        EnsureTransaction();
        if (productIds.Count == 0)
            return [];
        var ids = productIds.ToArray();
        return await db.Batches
            .FromSql($"""
                SELECT * FROM batches
                WHERE product_id = ANY({ids})
                  AND status = 'Active'
                  AND remaining_qty > 0
                  AND expires_at > {now}
                ORDER BY product_id, expires_at, id
                FOR UPDATE
                """)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Batch>> LockBatchesAsync(IReadOnlyCollection<Guid> batchIds, CancellationToken ct)
    {
        EnsureTransaction();
        if (batchIds.Count == 0)
            return [];
        var ids = batchIds.ToArray();
        return await db.Batches
            .FromSql($"""
                SELECT * FROM batches
                WHERE id = ANY({ids})
                ORDER BY product_id, expires_at, id
                FOR UPDATE
                """)
            .ToListAsync(ct);
    }

    public async Task<Order?> LockOrderAsync(Guid orderId, CancellationToken ct)
    {
        EnsureTransaction();
        return (await db.Orders.FromSql($"SELECT * FROM orders WHERE id = {orderId} FOR UPDATE").ToListAsync(ct)).SingleOrDefault();
    }

    public async Task AcquireTransactionLockAsync(string name, CancellationToken ct)
    {
        EnsureTransaction();
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({name}))", ct);
    }

    // Константа времени компиляции — без подстановки пользовательских данных.
    private const string NextOrderNumberSql = "SELECT nextval('" + OrderConfiguration.NumberSequence + "') AS \"Value\"";

    public async Task<long> NextOrderNumberAsync(CancellationToken ct) =>
        await db.Database.SqlQueryRaw<long>(NextOrderNumberSql).SingleAsync(ct);

    private void EnsureTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Блокировка строк требует открытой транзакции.");
    }
}
