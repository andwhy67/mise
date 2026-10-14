namespace Mise.Domain.Stock;

/// <summary>
/// Агрегат расхода (consumption_hourly, PLAN §3.1): чистое списание продукта под заказы за час (минус возвраты при отмене).
/// Read-model: строится из неизменяемого журнала движений, пересчитывается идемпотентно.
/// </summary>
public sealed class ConsumptionHour
{
    private ConsumptionHour() { }

    public Guid ProductId { get; private set; }
    /// <summary>Начало часа, UTC.</summary>
    public DateTimeOffset HourBucket { get; private set; }
    public decimal QtyConsumed { get; private set; }
}
