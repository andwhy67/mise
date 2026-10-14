using Mise.Domain.Alerts;
using Mise.Domain.Stock;

namespace Mise.Domain.Expiry;

/// <summary>Сигнал о партии: пересечённый уровень срока (Warning, Critical или Expired).</summary>
public sealed record ExpirySignal(Guid BatchId, Guid ProductId, ShelfLifeStatus Level, decimal HoursLeft)
{
    public string DedupKey => AlertKeys.Batch(BatchId, Level);
}

/// <param name="ToExpire">Активные партии, срок которых наступил → статус Expired.</param>
/// <param name="ToWriteOff">Партии, остаток которых списывается по сроку (только при включённом автосписании).</param>
/// <param name="Signals">Новые (ещё не поднятые) сигналы по уровням.</param>
public sealed record ExpiryPlan(IReadOnlyList<Batch> ToExpire, IReadOnlyList<Batch> ToWriteOff, IReadOnlyList<ExpirySignal> Signals)
{
    public bool IsEmpty => ToExpire.Count == 0 && ToWriteOff.Count == 0 && Signals.Count == 0;
}

/// <summary>
/// Решение одного тика мониторинга сроков (PLAN §4.3) — чистая функция без побочных эффектов.
/// Сигнал поднимается по текущему уровню партии: если тик «проскочил» Warning, сразу будет Critical.
/// Дубликаты отсекаются по уже поднятым ключам (<see cref="AlertKeys.Batch"/>).
/// </summary>
public static class ExpiryScanner
{
    public static ExpiryPlan Plan(
        IEnumerable<Batch> batches,
        IReadOnlySet<string> raisedKeys,
        DateTimeOffset now,
        ShelfLifeThresholds thresholds,
        bool autoWriteOff)
    {
        var toExpire = new List<Batch>();
        var toWriteOff = new List<Batch>();
        var signals = new List<ExpirySignal>();

        foreach (var batch in batches)
        {
            if (batch.RemainingQty <= 0)
                continue;

            ShelfLifeStatus level;
            if (batch.Status == BatchStatus.Active && now >= batch.ExpiresAt)
            {
                toExpire.Add(batch);
                if (autoWriteOff)
                    toWriteOff.Add(batch);
                level = ShelfLifeStatus.Expired;
            }
            else if (batch.Status == BatchStatus.Expired)
            {
                if (autoWriteOff)
                    toWriteOff.Add(batch);
                level = ShelfLifeStatus.Expired;
            }
            else if (batch.IsUsableAt(now))
            {
                level = ShelfLifeClassifier.Classify(batch.ReceivedAt, batch.ExpiresAt, now, thresholds).Status;
                if (level == ShelfLifeStatus.Fresh)
                    continue;
            }
            else
            {
                continue;
            }

            var hoursLeft = level == ShelfLifeStatus.Expired
                ? 0m
                : ShelfLifeClassifier.Classify(batch.ReceivedAt, batch.ExpiresAt, now, thresholds).HoursLeft;
            var signal = new ExpirySignal(batch.Id, batch.ProductId, level, hoursLeft);
            if (!raisedKeys.Contains(signal.DedupKey))
                signals.Add(signal);
        }

        return new ExpiryPlan(toExpire, toWriteOff, signals);
    }
}
