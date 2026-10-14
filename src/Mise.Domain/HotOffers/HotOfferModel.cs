using Mise.Domain.Common;

namespace Mise.Domain.HotOffers;

/// <summary>Параметры §4.4 (все — в конфиге, не хардкод). Значения по умолчанию — из PLAN.</summary>
public sealed record HotOfferParameters
{
    public static HotOfferParameters Default { get; } = new();

    /// <summary>Горизонт риска: H = min(<see cref="MaxHorizonHours"/>, <see cref="HorizonShelfFraction"/> × срок годности).</summary>
    public decimal MaxHorizonHours { get; init; } = 72m;
    public decimal HorizonShelfFraction { get; init; } = 0.5m;

    /// <summary>Давление продукта P = T × (w + (1 − w) × V): вес срочности без учёта объёма.</summary>
    public decimal VolumeBaseWeight { get; init; } = 0.5m;

    /// <summary>Вклад c = P × (w + (1 − w) × share): вес вклада без учёта доли блюда в продукте.</summary>
    public decimal ShareBaseWeight { get; init; } = 0.6m;

    /// <summary>Бонус за каждый следующий горящий ингредиент блюда (к главному).</summary>
    public decimal SecondaryBonus { get; init; } = 0.25m;

    /// <summary>
    /// Только v2: давление × (f + (1 − f) × доля, которая не успеет разойтись). Партия, которую обычный спрос разберёт
    /// целиком, сохраняет долю f давления — оценка скорости спроса неточна, совсем обнулять риск нельзя.
    /// </summary>
    public decimal SellThroughFloor { get; init; } = 0.5m;

    /// <summary>cap = min(1, maxPortions / N): блюдо на 1–2 порции запас не «сожжёт».</summary>
    public decimal FullCapacityPortions { get; init; } = 3m;

    /// <summary>Гистерезис: новое блюдо входит в топ при score ≥ Enter, уже показанное уходит при score &lt; Exit.</summary>
    public decimal EnterThreshold { get; init; } = 15m;
    public decimal ExitThreshold { get; init; } = 10m;

    public int TopN { get; init; } = 5;

    public HotOfferParameters Validate()
    {
        if (MaxHorizonHours <= 0 || HorizonShelfFraction is <= 0 or > 1)
            throw new ArgumentException("Горизонт: MaxHorizonHours > 0, HorizonShelfFraction в (0;1].");
        if (VolumeBaseWeight is < 0 or > 1 || ShareBaseWeight is < 0 or > 1 || SecondaryBonus is < 0 or > 1 || SellThroughFloor is < 0 or > 1)
            throw new ArgumentException("Веса VolumeBaseWeight, ShareBaseWeight, SecondaryBonus, SellThroughFloor — в [0;1].");
        if (FullCapacityPortions < 1)
            throw new ArgumentException("FullCapacityPortions ≥ 1.");
        if (ExitThreshold < 0 || ExitThreshold > EnterThreshold || EnterThreshold > 100)
            throw new ArgumentException("Пороги: 0 ≤ ExitThreshold ≤ EnterThreshold ≤ 100.");
        if (TopN < 1)
            throw new ArgumentException("TopN ≥ 1.");
        return this;
    }
}

/// <param name="DefaultShelfLifeHours">Норма срока из словаря; нет — горизонт считается от срока партии.</param>
/// <param name="MinStockQty">Базовый объём, если нет истории расхода.</param>
/// <param name="DailyConsumption">Среднесуточный расход по истории (0 — истории нет).</param>
public sealed record HotOfferProduct(
    Guid Id,
    string Name,
    MeasureUnit Unit,
    int? DefaultShelfLifeHours,
    decimal MinStockQty,
    decimal DailyConsumption)
{
    /// <summary>baseline_p (§4.4): расход за сутки, иначе минимальный остаток.</summary>
    public decimal Baseline => DailyConsumption > 0 ? DailyConsumption : MinStockQty;
}

/// <summary>Пригодная партия (непросроченная, с остатком).</summary>
public sealed record HotOfferBatch(Guid BatchId, Guid ProductId, DateTimeOffset ReceivedAt, DateTimeOffset ExpiresAt, decimal RemainingQty);

public sealed record HotOfferIngredient(Guid ProductId, decimal QtyPerPortion);

/// <param name="MaxPortions">Сколько порций можно приготовить сейчас (0 — блюдо в стопе и не рекомендуется).</param>
public sealed record HotOfferDish(Guid Id, string Name, int MaxPortions, IReadOnlyList<HotOfferIngredient> Ingredients);

public sealed record HotOfferInput(
    IReadOnlyList<HotOfferProduct> Products,
    IReadOnlyList<HotOfferBatch> Batches,
    IReadOnlyList<HotOfferDish> Dishes,
    DateTimeOffset Now);

/// <summary>Причина рекомендации — горящий ингредиент блюда (структурно, для UI и объяснения).</summary>
/// <param name="AtRiskQty">Остаток партий в горизонте риска.</param>
/// <param name="UnsoldQty">Из них не успеет разойтись при обычном спросе (только v2 и при наличии истории расхода).</param>
/// <param name="HoursLeft">До истечения самой срочной из учтённых партий.</param>
/// <param name="PortionsBurned">Сколько порций блюда можно сделать из сырья под риском (не больше доступных порций).</param>
/// <param name="Pressure">Давление продукта P (0…1).</param>
/// <param name="Share">Доля блюда в продукте среди доступных блюд.</param>
/// <param name="Contribution">Вклад в оценку c = P × (w + (1 − w) × share).</param>
public sealed record HotOfferReason(
    Guid ProductId,
    string ProductName,
    MeasureUnit Unit,
    decimal AtRiskQty,
    decimal? UnsoldQty,
    decimal HoursLeft,
    DateTimeOffset ExpiresAt,
    decimal QtyPerPortion,
    int PortionsBurned,
    decimal Pressure,
    decimal Share,
    decimal Contribution);

/// <param name="Score">0…100.</param>
/// <param name="Capacity">cap = min(1, maxPortions / N).</param>
/// <param name="Reasons">Горящие ингредиенты по убыванию вклада.</param>
public sealed record HotOffer(Guid DishId, string DishName, int MaxPortions, decimal Score, decimal Capacity, IReadOnlyList<HotOfferReason> Reasons);

/// <summary>Алгоритм оценки «что продавать в первую очередь» — версии сравниваются на одних и тех же данных.</summary>
public interface IHotOfferScorer
{
    string Version { get; }

    /// <summary>Все доступные блюда с горящими ингредиентами (score &gt; 0), по убыванию score, при равенстве — по id.</summary>
    IReadOnlyList<HotOffer> Score(HotOfferInput input, HotOfferParameters parameters);
}
