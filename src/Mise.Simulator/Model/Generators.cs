namespace Mise.Simulator.Model;

/// <summary>
/// Генератор заказов: блюда по весам категорий (хиты — паста, салаты, пицца; аутсайдеры — гарниры, завтраки),
/// 1–4 разных блюда в заказе, иногда по 2 порции.
/// </summary>
public static class OrderGenerator
{
    private static readonly Dictionary<string, double> CategoryWeights = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Паста"] = 3,
        ["Салаты"] = 2.5,
        ["Пицца"] = 2.5,
        ["Горячее"] = 2,
        ["Супы"] = 1.5,
        ["Завтраки"] = 1,
        ["Гарниры"] = 1,
    };

    /// <summary>Вероятности числа разных блюд в заказе: 1, 2, 3, 4.</summary>
    public static readonly double[] LineCountWeights = [0.5, 0.3, 0.15, 0.05];

    public const double DoublePortionProbability = 0.15;

    public static double WeightOf(DishInfo dish) =>
        dish.Category is { } category && CategoryWeights.TryGetValue(category, out var weight) ? weight : 1;

    public static IReadOnlyList<OrderLine> Next(Random random, IReadOnlyList<DishInfo> dishes)
    {
        var pool = dishes.Where(d => d.IsActive && d.Ingredients.Count > 0).ToList();
        if (pool.Count == 0)
            return [];

        var count = Math.Min(PickIndex(random, LineCountWeights) + 1, pool.Count);
        var lines = new List<OrderLine>(count);
        for (var i = 0; i < count; i++)
        {
            var index = PickIndex(random, pool.Select(WeightOf).ToArray());
            var dish = pool[index];
            pool.RemoveAt(index);
            lines.Add(new OrderLine(dish.Id, random.NextDouble() < DoublePortionProbability ? 2 : 1));
        }
        return lines;
    }

    public static int PickIndex(Random random, IReadOnlyList<double> weights)
    {
        var roll = random.NextDouble() * weights.Sum();
        for (var i = 0; i < weights.Count; i++)
        {
            roll -= weights[i];
            if (roll < 0)
                return i;
        }
        return weights.Count - 1;
    }
}

/// <summary>
/// Планировщик поставок: дозаказ до целевого запаса, когда доступно меньше точки заказа (1,5 × минимум).
/// Целевой запас — 3 × минимум (не меньше упаковки), количество кратно упаковке.
/// </summary>
public static class SupplyPlanner
{
    public const decimal ReorderFactor = 1.5m;
    public const decimal TargetFactor = 3m;
    /// <summary>Сценарий Spoilage портит только скоропорт (срок по умолчанию до недели), сухой склад приходит как обычно.</summary>
    public const int PerishableMaxHours = 168;
    public const int FallbackShelfLifeHours = 72;
    /// <summary>Позиций в одной поставке (у API лимит 200); самые дефицитные — первыми, остальное довезёт следующая.</summary>
    public const int MaxItemsPerSupply = 100;

    private static readonly string[] Suppliers = ["ООО «Свежесть»", "Рыбный двор", "Мясной цех", "Агроферма «Зелёная»", "Сухой склад №1"];

    /// <param name="fullRestock">Довезти всё до целевого запаса, независимо от точки заказа (FreshDelivery).</param>
    public static SupplyDraft? Plan(
        Random random,
        IReadOnlyList<ProductInfo> products,
        IReadOnlyDictionary<Guid, decimal> usable,
        ScenarioProfile profile,
        DateTimeOffset now,
        bool fullRestock)
    {
        var items = new List<(decimal Fill, SupplyLine Line)>();
        foreach (var product in products)
        {
            var pack = product.PackSize > 0 ? product.PackSize : 1m;
            var minimum = product.MinStockQty > 0 ? product.MinStockQty : pack;
            var target = Math.Max(minimum * TargetFactor, pack);
            var current = usable.GetValueOrDefault(product.Id);
            if (current >= target || (!fullRestock && current >= minimum * ReorderFactor))
                continue;

            var quantity = Math.Ceiling((target - current) / pack) * pack;
            var fill = current / target;
            var shelfLife = product.DefaultShelfLifeHours ?? FallbackShelfLifeHours;

            if (!fullRestock && profile.SpoilageMinutes is { } spoilage && shelfLife <= PerishableMaxHours)
            {
                var minutes = random.Next(spoilage.Min, spoilage.Max + 1);
                items.Add((fill, new SupplyLine(product.Id, quantity, null, now.AddMinutes(minutes))));
            }
            else
            {
                // Разброс срока: 80–100% от нормы (поставщики привозят не всегда самое свежее).
                var hours = Math.Max(1, (int)Math.Round(shelfLife * (0.8 + 0.2 * random.NextDouble())));
                items.Add((fill, new SupplyLine(product.Id, quantity, hours, null)));
            }
        }

        if (items.Count == 0)
            return null;
        var number = $"SUP-{now:yyyy}-{random.Next(0, 1_000_000):D6}";
        var lines = items.OrderBy(i => i.Fill).Take(MaxItemsPerSupply).Select(i => i.Line).ToList();
        return new SupplyDraft(number, Suppliers[random.Next(Suppliers.Length)], lines);
    }
}
