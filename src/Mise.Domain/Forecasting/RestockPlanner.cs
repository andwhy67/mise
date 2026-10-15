namespace Mise.Domain.Forecasting;

/// <summary>Блюдо в стопе по нехватке: состав на порцию и сколько порций в среднем продают в сутки.</summary>
public sealed record RestockDish(Guid DishId, IReadOnlyList<ForecastIngredient> Ingredients, decimal PortionsPerDay);

/// <param name="Usable">Пригодный остаток сейчас (базовые единицы).</param>
public sealed record RestockProduct(Guid ProductId, decimal Usable, decimal PackSize);

/// <summary>Ингредиент, которого не хватает на целевые порции блюда.</summary>
public sealed record RestockLine(Guid ProductId, decimal PerPortion, decimal Needed, decimal Usable, decimal Missing);

/// <param name="TargetPortions">Сколько порций вернуть в меню: прогноз продаж на горизонт, но не меньше минимума.</param>
public sealed record RestockDishPlan(Guid DishId, int TargetPortions, IReadOnlyList<RestockLine> Lines);

/// <param name="Missing">Не хватает на все блюда из стопа вместе.</param>
/// <param name="ToBuy">Сколько купить: недостача, округлённая вверх до упаковки.</param>
/// <param name="Dishes">Какие блюда из стопа вернёт эта закупка (вместе с остальными строками).</param>
public sealed record RestockPurchase(Guid ProductId, decimal Missing, decimal ToBuy, IReadOnlyList<Guid> Dishes);

public sealed record RestockPlan(IReadOnlyList<RestockDishPlan> Dishes, IReadOnlyList<RestockPurchase> Purchases);

/// <summary>
/// «Что докупить, чтобы вернуть блюда из стопа». Блюду нужно хватить на целевые порции — прогноз продаж на горизонт,
/// но не меньше минимума (вернуть блюдо на одну порцию — снова стоп через полчаса). Общий продукт считается
/// сразу на все блюда из стопа: по отдельности каждому может хватать, а вместе — нет.
/// </summary>
public static class RestockPlanner
{
    public static RestockPlan Plan(
        IReadOnlyList<RestockDish> stopped,
        IReadOnlyDictionary<Guid, RestockProduct> products,
        TimeSpan horizon,
        int minPortions)
    {
        decimal Usable(Guid id) => products.GetValueOrDefault(id)?.Usable ?? 0m;

        var targets = stopped.ToDictionary(d => d.DishId, d => Math.Max(minPortions,
            (int)Math.Ceiling(d.PortionsPerDay * (decimal)horizon.TotalHours / 24m)));

        var dishes = stopped.Select(d =>
        {
            var target = targets[d.DishId];
            var lines = d.Ingredients
                .Where(i => i.QtyPerPortion > 0)
                .Select(i => (Ingredient: i, Needed: i.QtyPerPortion * target, Usable: Usable(i.ProductId)))
                .Where(x => x.Usable < x.Needed)
                .Select(x => new RestockLine(x.Ingredient.ProductId, x.Ingredient.QtyPerPortion, x.Needed, x.Usable, x.Needed - x.Usable))
                .ToList();
            return new RestockDishPlan(d.DishId, target, lines);
        }).ToList();

        var purchases = stopped
            .SelectMany(d => d.Ingredients.Where(i => i.QtyPerPortion > 0).Select(i => (d.DishId, i.ProductId, Needed: i.QtyPerPortion * targets[d.DishId])))
            .GroupBy(x => x.ProductId)
            .Select(g =>
            {
                var missing = g.Sum(x => x.Needed) - Usable(g.Key);
                var pack = products.GetValueOrDefault(g.Key)?.PackSize is > 0 and var size ? size : 1m;
                return new RestockPurchase(g.Key, missing, Math.Ceiling(missing / pack) * pack, g.Select(x => x.DishId).Distinct().ToList());
            })
            .Where(p => p.Missing > 0)
            .ToList();

        return new RestockPlan(dishes, purchases);
    }
}
