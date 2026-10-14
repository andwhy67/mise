using Mise.Domain.Common;
using Mise.Domain.HotOffers;

namespace Mise.Domain.Tests.HotOffers;

/// <summary>Короткая запись сценариев scoring: продукты, партии «истекает через N ч», блюда с составом и порциями.</summary>
internal sealed class HotOfferScenarioBuilder
{
    public static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly List<HotOfferProduct> _products = [];
    private readonly List<HotOfferBatch> _batches = [];
    private readonly List<HotOfferDish> _dishes = [];
    private readonly Dictionary<string, Guid> _ids = [];

    public Guid Id(string name) => _ids.TryGetValue(name, out var id) ? id : _ids[name] = Guid.NewGuid();

    public HotOfferScenarioBuilder Product(string name, decimal minStock = 1000, int? shelfLifeHours = 168,
        decimal dailyConsumption = 0, MeasureUnit unit = MeasureUnit.Gram)
    {
        _products.Add(new HotOfferProduct(Id(name), name, unit, shelfLifeHours, minStock, dailyConsumption));
        return this;
    }

    /// <summary>
    /// Партия истекает через <paramref name="hoursLeft"/> (отрицательное — уже истекла); получена <paramref name="ageHours"/> назад —
    /// по умолчанию так, чтобы её полный срок совпадал с нормой продукта.
    /// </summary>
    public HotOfferScenarioBuilder Batch(string product, decimal qty, double hoursLeft, double? ageHours = null)
    {
        var shelfLife = _products.Single(p => p.Id == Id(product)).DefaultShelfLifeHours ?? 168;
        var age = ageHours ?? shelfLife - hoursLeft;
        _batches.Add(new HotOfferBatch(Guid.NewGuid(), Id(product), Now.AddHours(-age), Now.AddHours(hoursLeft), qty));
        return this;
    }

    public HotOfferScenarioBuilder Dish(string name, int maxPortions, params (string Product, decimal Qty)[] ingredients)
    {
        _dishes.Add(new HotOfferDish(Id(name), name, maxPortions,
            ingredients.Select(i => new HotOfferIngredient(Id(i.Product), i.Qty)).ToList()));
        return this;
    }

    public HotOfferInput Build() => new(_products, _batches, _dishes, Now);

    public IReadOnlyList<HotOffer> Score(IHotOfferScorer? scorer = null, HotOfferParameters? parameters = null) =>
        (scorer ?? new HotOfferScorerV2()).Score(Build(), parameters ?? HotOfferParameters.Default);

    public decimal ScoreOf(string dish, IHotOfferScorer? scorer = null) =>
        Score(scorer).FirstOrDefault(o => o.DishId == Id(dish))?.Score ?? 0m;
}
