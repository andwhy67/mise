namespace Mise.Domain.Forecasting;

/// <summary>Почему прогнозный остаток падает: его съели заказы или остаток партии истёк.</summary>
public enum ProjectionCause
{
    Consumption,
    Expiry,
}

/// <summary>Пригодная партия для прогноза.</summary>
public sealed record ForecastBatch(Guid BatchId, DateTimeOffset ExpiresAt, decimal RemainingQty);

/// <summary>Точка прогноза: остаток после изменения, пришедшего к моменту <see cref="At"/>.</summary>
public readonly record struct ProjectionPoint(DateTimeOffset At, decimal Usable, ProjectionCause Cause);

public readonly record struct ProjectionCrossing(DateTimeOffset At, ProjectionCause Cause);

/// <summary>Прогноз пригодного остатка продукта на горизонте.</summary>
public sealed class StockProjection(
    IReadOnlyList<ProjectionPoint> points,
    decimal consumed,
    decimal wasted,
    decimal unmetDemand,
    DateTimeOffset? runsOutAt,
    decimal eventServed = 0m)
{
    /// <summary>Остаток во времени: между точками — линейно (расход), на истечении — две точки в один момент (скачок).</summary>
    public IReadOnlyList<ProjectionPoint> Points { get; } = points;

    public decimal InitialUsable => Points[0].Usable;

    /// <summary>Сколько текущего остатка уйдёт в заказы на горизонте.</summary>
    public decimal Consumed { get; } = consumed;

    /// <summary>Сколько текущего остатка истечёт неиспользованным на горизонте.</summary>
    public decimal Wasted { get; } = wasted;

    /// <summary>Спрос, который нечем покрыть.</summary>
    public decimal UnmetDemand { get; } = unmetDemand;

    /// <summary>Когда спрос впервые перестанет покрываться (null — хватит или спроса нет).</summary>
    public DateTimeOffset? RunsOutAt { get; } = runsOutAt;

    /// <summary>Первый момент, когда остаток станет меньше порога (например, меньше одной порции); null — не на горизонте.</summary>
    public ProjectionCrossing? FirstTimeBelow(decimal threshold)
    {
        if (Points[0].Usable < threshold)
            return new ProjectionCrossing(Points[0].At, ProjectionCause.Consumption);

        for (var i = 1; i < Points.Count; i++)
        {
            var previous = Points[i - 1];
            var next = Points[i];
            if (next.Usable >= threshold || previous.Usable < threshold)
                continue;
            if (next.Cause == ProjectionCause.Expiry || next.At == previous.At)
                return new ProjectionCrossing(next.At, ProjectionCause.Expiry);

            var fraction = (previous.Usable - threshold) / (previous.Usable - next.Usable);
            return new ProjectionCrossing(previous.At + Scale(next.At - previous.At, fraction), ProjectionCause.Consumption);
        }
        return null;
    }

    /// <summary>Остаток на момент <paramref name="at"/> (после всех изменений, пришедших к этому моменту).</summary>
    public decimal UsableAt(DateTimeOffset at)
    {
        if (at <= Points[0].At)
            return Points[0].Usable;
        for (var i = 1; i < Points.Count; i++)
        {
            var previous = Points[i - 1];
            var next = Points[i];
            if (at > next.At || (at == next.At && i + 1 < Points.Count && Points[i + 1].At == at))
                continue;
            if (at == next.At || next.At == previous.At)
                return next.Usable;
            var fraction = (decimal)(at - previous.At).Ticks / (next.At - previous.At).Ticks;
            return previous.Usable + (next.Usable - previous.Usable) * fraction;
        }
        return Points[^1].Usable;
    }

    /// <summary>Сколько разового события (банкета) покрыто остатком в его момент.</summary>
    public decimal EventServed { get; } = eventServed;

    internal static TimeSpan Scale(TimeSpan span, decimal fraction) => TimeSpan.FromTicks((long)decimal.Round(span.Ticks * fraction));
}

/// <summary>
/// Прогноз остатка (PLAN §4.5): ожидаемый спрос съедает партии по FEFO, на <c>expires_at</c> несъеденный остаток
/// партии пропадает. Шаг — <paramref name="step"/>, плюс точные моменты истечения партий.
/// </summary>
public static class StockProjector
{
    /// <param name="oneOff">Разовый расход (банкет) в момент At — сверх обычного спроса, тоже по FEFO.</param>
    public static StockProjection Project(IReadOnlyList<ForecastBatch> batches, ConsumptionRate rate, DateTimeOffset now,
        TimeSpan horizon, TimeSpan step, (DateTimeOffset At, decimal Qty)? oneOff = null)
    {
        if (step <= TimeSpan.Zero || horizon <= TimeSpan.Zero)
            throw new ArgumentException("Шаг и горизонт прогноза должны быть положительными.");

        var alive = batches
            .Where(b => b.RemainingQty > 0 && b.ExpiresAt > now)
            .OrderBy(b => b.ExpiresAt).ThenBy(b => b.BatchId)
            .Select(b => new Slot(b.ExpiresAt, b.RemainingQty))
            .ToList();

        var points = new List<ProjectionPoint> { new(now, alive.Sum(s => s.Qty), ProjectionCause.Consumption) };
        decimal consumed = 0, wasted = 0, unmet = 0, eventServed = 0;
        DateTimeOffset? runsOutAt = null;
        var end = now + horizon;
        var t = now;

        while (t < end)
        {
            var next = t + step < end ? t + step : end;
            if (alive.Count > 0 && alive[0].ExpiresAt < next)
                next = alive.Min(s => s.ExpiresAt);
            if (oneOff is { At: var eventAt } && eventAt > t && eventAt < next)
                next = eventAt;

            var demand = rate.Expected(t, next);
            var taken = 0m;
            foreach (var slot in alive)
            {
                if (taken >= demand)
                    break;
                var take = Math.Min(slot.Qty, demand - taken);
                slot.Qty -= take;
                taken += take;
            }
            alive.RemoveAll(s => s.Qty <= 0);
            consumed += taken;
            if (demand > taken)
            {
                unmet += demand - taken;
                // Спрос равномерен внутри шага: запас кончился на доле taken / demand шага.
                runsOutAt ??= t + StockProjection.Scale(next - t, demand == 0 ? 0 : taken / demand);
            }
            points.Add(new ProjectionPoint(next, alive.Sum(s => s.Qty), ProjectionCause.Consumption));

            // Событие в момент next: обычный спрос до него уже съеден, событие берёт своё по FEFO.
            if (oneOff is { } single && single.At > t && single.At <= next && single.Qty > 0)
            {
                foreach (var slot in alive)
                {
                    if (eventServed >= single.Qty)
                        break;
                    var take = Math.Min(slot.Qty, single.Qty - eventServed);
                    slot.Qty -= take;
                    eventServed += take;
                }
                alive.RemoveAll(s => s.Qty <= 0);
                points.Add(new ProjectionPoint(next, alive.Sum(s => s.Qty), ProjectionCause.Consumption));
            }

            var expiring = alive.Where(s => s.ExpiresAt <= next).ToList();
            if (expiring.Count > 0)
            {
                wasted += expiring.Sum(s => s.Qty);
                alive.RemoveAll(s => s.ExpiresAt <= next);
                points.Add(new ProjectionPoint(next, alive.Sum(s => s.Qty), ProjectionCause.Expiry));
            }

            t = next;
        }

        return new StockProjection(points, consumed, wasted, unmet, runsOutAt, eventServed);
    }

    private sealed class Slot(DateTimeOffset expiresAt, decimal qty)
    {
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public decimal Qty { get; set; } = qty;
    }
}
