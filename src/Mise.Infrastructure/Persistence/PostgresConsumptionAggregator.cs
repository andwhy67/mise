using Microsoft.EntityFrameworkCore;
using Mise.Application.Abstractions;

namespace Mise.Infrastructure.Persistence;

/// <summary>
/// Пересчёт consumption_hourly из журнала движений одним запросом. Движения неизменяемы и пишутся «сейчас», поэтому
/// достаточно пересчитать часы начиная с последнего записанного (с запасом в час — на транзакции, закоммиченные позже
/// своего created_at). Пересчёт идемпотентен (ON CONFLICT … = EXCLUDED), инстансы сериализуются advisory lock-ом.
/// </summary>
internal sealed class PostgresConsumptionAggregator(MiseDbContext db) : IConsumptionAggregator
{
    private const string LockName = "consumption-aggregator";

    // Константа без пользовательских данных.
    private const string RefreshSql = """
        INSERT INTO consumption_hourly (product_id, hour_bucket, qty_consumed)
        SELECT m.product_id, date_trunc('hour', m.created_at, 'UTC'), -SUM(m.qty_delta)
        FROM stock_movements m
        WHERE m.type IN ('OrderConsumption', 'OrderCancelReturn')
          AND m.created_at >= COALESCE((SELECT MAX(hour_bucket) FROM consumption_hourly) - interval '1 hour', '-infinity'::timestamptz)
        GROUP BY 1, 2
        ON CONFLICT (product_id, hour_bucket) DO UPDATE SET qty_consumed = EXCLUDED.qty_consumed
        """;

    public async Task<int> RefreshAsync(CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({LockName}))", ct);
        var rows = await db.Database.ExecuteSqlRawAsync(RefreshSql, ct);
        await transaction.CommitAsync(ct);
        return rows;
    }
}
