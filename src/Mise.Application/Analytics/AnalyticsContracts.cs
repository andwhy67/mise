using Mise.Domain.Common;
using Mise.Domain.Stock;

namespace Mise.Application.Analytics;

/// <summary>Период отчёта [From; To). По умолчанию — последние 7 суток.</summary>
public sealed record AnalyticsPeriod(DateTimeOffset From, DateTimeOffset To)
{
    public decimal Minutes => (decimal)(To - From).TotalMinutes;
}

public enum ConsumptionBucket
{
    Hour,
    /// <summary>Сутки по Москве.</summary>
    Day,
}

// ---------- Списания ----------

/// <param name="Date">Дата по Москве.</param>
/// <param name="Reason">ExpiryWriteOff — по сроку, ManualWriteOff — вручную.</param>
public sealed record WriteOffItemDto(DateOnly Date, Guid ProductId, string ProductName, MeasureUnit Unit, MovementType Reason, decimal Qty, int Operations);

public sealed record WriteOffByProductDto(Guid ProductId, string ProductName, MeasureUnit Unit, decimal ExpiryQty, decimal ManualQty, decimal TotalQty, int Operations);

public sealed record WriteOffByDayDto(DateOnly Date, int ExpiryOperations, int ManualOperations, int Products);

public sealed record WriteOffReportDto(
    DateTimeOffset From,
    DateTimeOffset To,
    int ExpiryOperations,
    int ManualOperations,
    IReadOnlyList<WriteOffByDayDto> ByDay,
    IReadOnlyList<WriteOffByProductDto> ByProduct,
    IReadOnlyList<WriteOffItemDto> Items);

// ---------- Дефицит ----------

/// <param name="StopEvents">Сколько раз продукт отправлял блюда в стоп (начала интервалов в периоде).</param>
/// <param name="DishMinutesInStop">Сумма минут блюд в стопе из-за продукта (пересечение с периодом).</param>
/// <param name="MinutesBelowMinimum">Сколько минут продукт был ниже минимального остатка (по алертам LowStock).</param>
/// <param name="ShareBelowMinimum">Доля периода ниже минимума, 0…1.</param>
/// <param name="Refusals">Сколько заказов отклонено (422) из-за нехватки продукта.</param>
/// <param name="ShortageQty">Сколько продукта не хватило суммарно по этим отказам (базовые единицы).</param>
public sealed record DeficitItemDto(
    Guid ProductId,
    string ProductName,
    MeasureUnit Unit,
    int Refusals,
    decimal ShortageQty,
    DateTimeOffset? LastRefusalAt,
    int StopEvents,
    int DishesAffected,
    decimal DishMinutesInStop,
    decimal MinutesBelowMinimum,
    decimal ShareBelowMinimum);

public sealed record DeficitReportDto(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<DeficitItemDto> Items);

// ---------- Расход ----------

public sealed record ConsumptionPointDto(DateTimeOffset Bucket, decimal Qty);

public sealed record ConsumptionSeriesDto(Guid ProductId, string ProductName, MeasureUnit Unit, decimal Total, IReadOnlyList<ConsumptionPointDto> Points);

public sealed record ConsumptionReportDto(DateTimeOffset From, DateTimeOffset To, ConsumptionBucket Bucket, IReadOnlyList<ConsumptionSeriesDto> Series);

// ---------- KPI ----------

public sealed record WriteOffRateDto(Guid ProductId, string ProductName, MeasureUnit Unit, decimal ReceivedQty, decimal WrittenOffQty, decimal Rate);

/// <param name="WriteOffRate">Доля списанного от поступившего: среднее по продуктам с поставками в периоде (единицы у продуктов разные).</param>
/// <param name="AverageDishesInStop">Среднее число блюд в стопе за период.</param>
public sealed record KpiPeriodDto(
    int Orders,
    int CancelledOrders,
    decimal Revenue,
    decimal AverageTicket,
    decimal WriteOffRate,
    IReadOnlyList<WriteOffRateDto> WorstWriteOffRates,
    decimal DishMinutesInStop,
    decimal AverageDishesInStop);

/// <summary>Плитки дашборда (PLAN §10): «сейчас» и «сегодня» (сутки по Москве).</summary>
public sealed record KpiNowDto(int DishesInStop, int BatchesExpiringWithin24h, int ProductsBelowMinimum, int WriteOffsToday, int OrdersToday, decimal RevenueToday);

public sealed record KpiReportDto(DateTimeOffset From, DateTimeOffset To, KpiPeriodDto Period, KpiNowDto Now);
