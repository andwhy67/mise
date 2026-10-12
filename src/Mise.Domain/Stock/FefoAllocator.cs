namespace Mise.Domain.Stock;

public readonly record struct BatchAllocation(Guid BatchId, decimal Quantity);

/// <param name="Available">Сколько всего пригодно в переданных партиях.</param>
/// <param name="Shortage">Сколько не хватает (0, если потребность покрыта).</param>
public sealed record AllocationResult(IReadOnlyList<BatchAllocation> Allocations, decimal Available, decimal Shortage)
{
    public bool IsSatisfied => Shortage == 0m;
}

/// <summary>
/// FEFO (first expired — first out): потребность покрывается партиями с самым ранним expires_at.
/// Просроченные и израсходованные партии не участвуют. При нехватке распределение не выдаётся вовсе —
/// заказ отклоняется целиком, ничего не списывается.
/// </summary>
public static class FefoAllocator
{
    public static AllocationResult Allocate(decimal required, IEnumerable<Batch> batches, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(required);

        var usable = batches
            .Where(b => b.IsUsableAt(now))
            .OrderBy(b => b.ExpiresAt).ThenBy(b => b.Id)
            .ToList();
        var available = usable.Sum(b => b.RemainingQty);
        if (available < required)
            return new AllocationResult([], available, required - available);

        var allocations = new List<BatchAllocation>();
        var left = required;
        foreach (var batch in usable)
        {
            var take = Math.Min(batch.RemainingQty, left);
            allocations.Add(new BatchAllocation(batch.Id, take));
            left -= take;
            if (left == 0m)
                break;
        }

        return new AllocationResult(allocations, available, 0m);
    }
}
