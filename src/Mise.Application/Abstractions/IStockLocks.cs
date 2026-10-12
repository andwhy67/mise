using Mise.Domain.Orders;
using Mise.Domain.Stock;

namespace Mise.Application.Abstractions;

/// <summary>
/// Пессимистические блокировки для операций с остатками (CLAUDE.md, правило 5).
/// Все методы требуют открытой транзакции; строки блокируются до её завершения.
/// </summary>
public interface IStockLocks
{
    /// <summary>
    /// <c>SELECT … FOR UPDATE</c> пригодных партий (Active, остаток &gt; 0, expires_at &gt; now) указанных продуктов
    /// в детерминированном порядке <c>product_id, expires_at, id</c> — без дедлоков между параллельными заказами.
    /// </summary>
    Task<IReadOnlyList<Batch>> LockUsableBatchesAsync(IReadOnlyCollection<Guid> productIds, DateTimeOffset now, CancellationToken ct);

    /// <summary><c>SELECT … FOR UPDATE</c> партий по id в том же детерминированном порядке.</summary>
    Task<IReadOnlyList<Batch>> LockBatchesAsync(IReadOnlyCollection<Guid> batchIds, CancellationToken ct);

    /// <summary><c>SELECT … FOR UPDATE</c> заказа (сериализует отмену и смену статуса).</summary>
    Task<Order?> LockOrderAsync(Guid orderId, CancellationToken ct);

    /// <summary>Транзакционный advisory lock по имени (<c>pg_advisory_xact_lock</c>) — снимается при commit/rollback.</summary>
    Task AcquireTransactionLockAsync(string name, CancellationToken ct);

    /// <summary>Следующий номер заказа из последовательности БД.</summary>
    Task<long> NextOrderNumberAsync(CancellationToken ct);
}
