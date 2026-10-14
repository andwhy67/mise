using Mise.Domain.Common;

namespace Mise.Domain.HotOffers;

/// <summary>Группа блюда в «Что продавать».</summary>
public enum MenuGroup
{
    /// <summary>Спасает продукты, которые скоро испортятся (горячее предложение, score ≥ порога выхода).</summary>
    Saves,
    /// <summary>Разгружает склад: скоропортящегося продукта больше, чем обычно уходит за несколько дней.</summary>
    Overstock,
    /// <summary>Остальное доступное меню.</summary>
    Regular,
}

/// <summary>
/// Пороги «разгрузить склад» (секция HotOffers, конфиг): запас продукта от <see cref="OverstockDays"/> дней обычного
/// спроса — повод предлагать блюдо, если продукт скоропортящийся (норма хранения ≤ <see cref="PerishableMaxDays"/>).
/// Соль на полгода — не повод: она и так не пропадёт.
/// </summary>
public sealed record MenuRankingParameters(decimal OverstockDays, decimal PerishableMaxDays)
{
    public static MenuRankingParameters Default { get; } = new(4m, 30m);
}

/// <summary>Чем блюдо разгружает склад: продукт, его запас и на сколько дней обычного спроса его хватит.</summary>
public sealed record StockCover(Guid ProductId, string ProductName, MeasureUnit Unit, decimal UsableQty, decimal DailyConsumption, decimal CoverDays);

public sealed record RankedDish(Guid DishId, string DishName, int MaxPortions, MenuGroup Group, HotOffer? Offer, StockCover? Cover);

/// <summary>
/// Всё доступное меню по порядку «что предлагать»: сначала спасающие продукты (по score), затем разгружающие склад
/// (по запасу в днях), затем остальное (по числу порций). Блюда в стопе не попадают.
/// </summary>
public static class MenuRanking
{
    public static IReadOnlyList<RankedDish> Rank(HotOfferInput input, IReadOnlyList<HotOffer> scored, HotOfferParameters offers, MenuRankingParameters parameters)
    {
        var available = input.Dishes.Where(d => d.MaxPortions >= 1).ToList();
        var offerByDish = scored.Where(o => o.Score >= offers.ExitThreshold).ToDictionary(o => o.DishId);

        var usable = input.Batches.Where(b => b.ExpiresAt > input.Now && b.RemainingQty > 0)
            .GroupBy(b => b.ProductId).ToDictionary(g => g.Key, g => g.Sum(b => b.RemainingQty));
        var covers = input.Products
            .Where(p => p.DailyConsumption > 0 && usable.GetValueOrDefault(p.Id) > 0
                        && p.DefaultShelfLifeHours is { } hours && hours <= parameters.PerishableMaxDays * 24)
            .Select(p => new StockCover(p.Id, p.Name, p.Unit, usable[p.Id], p.DailyConsumption, decimal.Round(usable[p.Id] / p.DailyConsumption, 1)))
            .Where(c => c.CoverDays >= parameters.OverstockDays)
            .ToDictionary(c => c.ProductId);

        var saves = available.Where(d => offerByDish.ContainsKey(d.Id))
            .Select(d => new RankedDish(d.Id, d.Name, d.MaxPortions, MenuGroup.Saves, offerByDish[d.Id], null))
            .OrderByDescending(r => r.Offer!.Score).ThenBy(r => r.DishName);

        var rest = available.Where(d => !offerByDish.ContainsKey(d.Id)).ToList();
        var overstock = rest
            .Select(d => (Dish: d, Cover: d.Ingredients.Select(i => covers.GetValueOrDefault(i.ProductId)).OfType<StockCover>()
                .OrderByDescending(c => c.CoverDays).FirstOrDefault()))
            .Where(x => x.Cover is not null)
            .Select(x => new RankedDish(x.Dish.Id, x.Dish.Name, x.Dish.MaxPortions, MenuGroup.Overstock, null, x.Cover))
            .OrderByDescending(r => r.Cover!.CoverDays).ThenByDescending(r => r.MaxPortions).ThenBy(r => r.DishName)
            .ToList();

        var overstockIds = overstock.Select(o => o.DishId).ToHashSet();
        var regular = rest.Where(d => !overstockIds.Contains(d.Id))
            .Select(d => new RankedDish(d.Id, d.Name, d.MaxPortions, MenuGroup.Regular, null, null))
            .OrderByDescending(r => r.MaxPortions).ThenBy(r => r.DishName);

        return [.. saves, .. overstock, .. regular];
    }
}
