using Mise.Domain.Catalog;
using Mise.Domain.Common;

namespace Mise.Infrastructure.Seed;

/// <summary>
/// Стартовый словарь: продукты и блюда с общими ингредиентами (сливки, пармезан, томаты, курица, лосось…),
/// чтобы демо сразу показывало интересные пересечения в стоп-листе и hot offers.
/// Идентификаторы фиксированные — на них опираются тесты и симулятор.
/// </summary>
public static class SeedData
{
    /// <summary>Демо-ресторан: демо-учётки, стартовый словарь и симулятор. В него же переехали данные до появления ресторанов.</summary>
    public static readonly Guid DemoRestaurantId = Guid.Parse("5e7a0000-0000-4000-8000-00000000d3e0");
    public const string DemoRestaurantName = "Mise · демо";

    public static class Products
    {
        public static readonly Guid Cream = P(1);
        public static readonly Guid Milk = P(2);
        public static readonly Guid Butter = P(3);
        public static readonly Guid Parmesan = P(4);
        public static readonly Guid Mozzarella = P(5);
        public static readonly Guid Feta = P(6);
        public static readonly Guid Eggs = P(7);
        public static readonly Guid Chicken = P(8);
        public static readonly Guid Salmon = P(9);
        public static readonly Guid Shrimp = P(10);
        public static readonly Guid Beef = P(11);
        public static readonly Guid Bacon = P(12);
        public static readonly Guid Tomatoes = P(13);
        public static readonly Guid Cucumbers = P(14);
        public static readonly Guid Romaine = P(15);
        public static readonly Guid Arugula = P(16);
        public static readonly Guid Basil = P(17);
        public static readonly Guid Spinach = P(18);
        public static readonly Guid Mushrooms = P(19);
        public static readonly Guid Potatoes = P(20);
        public static readonly Guid Onion = P(21);
        public static readonly Guid Garlic = P(22);
        public static readonly Guid Lemons = P(23);
        public static readonly Guid Spaghetti = P(24);
        public static readonly Guid Arborio = P(25);
        public static readonly Guid Flour = P(26);
    }

    public static class Dishes
    {
        public static readonly Guid Carbonara = D(1);
        public static readonly Guid SalmonPasta = D(2);
        public static readonly Guid MushroomRisotto = D(3);
        public static readonly Guid CaesarChicken = D(4);
        public static readonly Guid CaesarShrimp = D(5);
        public static readonly Guid GreekSalad = D(6);
        public static readonly Guid Caprese = D(7);
        public static readonly Guid ArugulaShrimp = D(8);
        public static readonly Guid BeefSteak = D(9);
        public static readonly Guid GrilledSalmon = D(10);
        public static readonly Guid MushroomSoup = D(11);
        public static readonly Guid ChickenSoup = D(12);
        public static readonly Guid SpinachOmelette = D(13);
        public static readonly Guid Pancakes = D(14);
        public static readonly Guid Margherita = D(15);
        public static readonly Guid MashedPotatoes = D(16);
        public static readonly Guid ChickenSpinachPasta = D(17);
    }

    private const string Dairy = "Молочные";
    private const string MeatFish = "Мясо и рыба";
    private const string Vegetables = "Овощи и зелень";
    private const string Grocery = "Бакалея";
    private const string Fridge = "Холодильник";
    private const string Dry = "Сухой склад";

    /// <summary>Замены при нехватке (base-единицы): зелень между собой, моцарелла → фета (её солонее — берём меньше), креветки → лосось.</summary>
    public static readonly IReadOnlyList<(Guid ProductId, Guid SubstituteId, decimal Ratio)> Substitutes =
    [
        (Products.Spinach, Products.Arugula, 1m),
        (Products.Arugula, Products.Spinach, 1m),
        (Products.Romaine, Products.Arugula, 1m),
        (Products.Mozzarella, Products.Feta, 0.8m),
        (Products.Shrimp, Products.Salmon, 1m),
        (Products.Cream, Products.Milk, 1.2m),
        (Products.Basil, Products.Arugula, 1m),
    ];

    public static IReadOnlyList<Product> CreateProducts() => WithSubstitutes(

    [
        Product(Products.Cream, "Сливки 33%", MeasureUnit.Milliliter, 168, 2000, 1000, Dairy, Fridge, 550m, "Молочный двор"),
        Product(Products.Milk, "Молоко 3,2%", MeasureUnit.Milliliter, 120, 3000, 1000, Dairy, Fridge, 95m, "Молочный двор"),
        Product(Products.Butter, "Масло сливочное 82%", MeasureUnit.Gram, 336, 1000, 500, Dairy, Fridge, 950m, "Молочный двор"),
        Product(Products.Parmesan, "Пармезан", MeasureUnit.Gram, 720, 500, 1000, Dairy, Fridge, 2600m, "Молочный двор"),
        Product(Products.Mozzarella, "Моцарелла", MeasureUnit.Gram, 120, 1000, 1000, Dairy, Fridge, 1100m, "Молочный двор"),
        Product(Products.Feta, "Сыр фета", MeasureUnit.Gram, 240, 500, 500, Dairy, Fridge, 1200m, "Молочный двор"),
        Product(Products.Eggs, "Яйца куриные С0", MeasureUnit.Piece, 336, 30, 30, Dairy, Fridge, 14m, "Молочный двор"),
        Product(Products.Chicken, "Куриное филе охл.", MeasureUnit.Gram, 72, 3000, 1000, MeatFish, Fridge, 520m, "Мясной цех"),
        Product(Products.Salmon, "Лосось охл.", MeasureUnit.Gram, 72, 2000, 1000, MeatFish, Fridge, 1900m, "Рыбный двор"),
        Product(Products.Shrimp, "Креветки очищенные", MeasureUnit.Gram, 48, 1000, 1000, MeatFish, Fridge, 1600m, "Рыбный двор"),
        Product(Products.Beef, "Говяжья вырезка", MeasureUnit.Gram, 96, 2000, 1000, MeatFish, Fridge, 2700m, "Мясной цех"),
        Product(Products.Bacon, "Бекон", MeasureUnit.Gram, 168, 500, 500, MeatFish, Fridge, 900m, "Мясной цех"),
        Product(Products.Tomatoes, "Томаты", MeasureUnit.Gram, 120, 3000, 1000, Vegetables, Fridge, 280m, "ООО «Свежесть»"),
        Product(Products.Cucumbers, "Огурцы", MeasureUnit.Gram, 120, 2000, 1000, Vegetables, Fridge, 220m, "ООО «Свежесть»"),
        Product(Products.Romaine, "Салат романо", MeasureUnit.Gram, 72, 1000, 500, Vegetables, Fridge, 450m, "Агроферма «Зелёная»"),
        Product(Products.Arugula, "Руккола", MeasureUnit.Gram, 48, 500, 250, Vegetables, Fridge, 1400m, "Агроферма «Зелёная»"),
        Product(Products.Basil, "Базилик", MeasureUnit.Gram, 72, 100, 100, Vegetables, Fridge, 2400m, "Агроферма «Зелёная»"),
        Product(Products.Spinach, "Шпинат", MeasureUnit.Gram, 72, 500, 250, Vegetables, Fridge, 900m, "Агроферма «Зелёная»"),
        Product(Products.Mushrooms, "Шампиньоны", MeasureUnit.Gram, 96, 1000, 500, Vegetables, Fridge, 350m, "ООО «Свежесть»"),
        Product(Products.Potatoes, "Картофель", MeasureUnit.Gram, 720, 5000, 5000, Vegetables, Dry, 55m, "ООО «Свежесть»"),
        Product(Products.Onion, "Лук репчатый", MeasureUnit.Gram, 720, 2000, 1000, Vegetables, Dry, 45m, "ООО «Свежесть»"),
        Product(Products.Garlic, "Чеснок", MeasureUnit.Gram, 720, 300, 500, Vegetables, Dry, 400m, "ООО «Свежесть»"),
        Product(Products.Lemons, "Лимоны", MeasureUnit.Piece, 336, 10, 10, Vegetables, Fridge, 25m, "ООО «Свежесть»"),
        Product(Products.Spaghetti, "Спагетти", MeasureUnit.Gram, 8760, 2000, 1000, Grocery, Dry, 230m, "Сухой склад №1"),
        Product(Products.Arborio, "Рис арборио", MeasureUnit.Gram, 8760, 2000, 1000, Grocery, Dry, 420m, "Сухой склад №1"),
        Product(Products.Flour, "Мука пшеничная", MeasureUnit.Gram, 4320, 2000, 1000, Grocery, Dry, 60m, "Сухой склад №1"),
    ]);

    public static IReadOnlyList<Dish> CreateDishes() =>
    [
        Dish(Dishes.Carbonara, "Паста карбонара", "Паста", 690m,
            (Products.Spaghetti, 120), (Products.Bacon, 60), (Products.Eggs, 2), (Products.Parmesan, 30), (Products.Cream, 50)),
        Dish(Dishes.SalmonPasta, "Паста с лососем в сливочном соусе", "Паста", 790m,
            (Products.Spaghetti, 120), (Products.Salmon, 100), (Products.Cream, 100), (Products.Garlic, 5)),
        Dish(Dishes.ChickenSpinachPasta, "Паста с курицей и шпинатом", "Паста", 650m,
            (Products.Spaghetti, 120), (Products.Chicken, 120), (Products.Spinach, 50), (Products.Cream, 80), (Products.Parmesan, 20)),
        Dish(Dishes.MushroomRisotto, "Ризотто с грибами", "Горячее", 650m,
            (Products.Arborio, 90), (Products.Mushrooms, 120), (Products.Cream, 50), (Products.Parmesan, 30), (Products.Butter, 20), (Products.Onion, 30)),
        Dish(Dishes.CaesarChicken, "Цезарь с курицей", "Салаты", 590m,
            (Products.Romaine, 120), (Products.Chicken, 120), (Products.Parmesan, 20), (Products.Eggs, 1), (Products.Garlic, 3)),
        Dish(Dishes.CaesarShrimp, "Цезарь с креветками", "Салаты", 720m,
            (Products.Romaine, 120), (Products.Shrimp, 100), (Products.Parmesan, 20), (Products.Eggs, 1), (Products.Garlic, 3)),
        Dish(Dishes.GreekSalad, "Греческий салат", "Салаты", 490m,
            (Products.Tomatoes, 120), (Products.Cucumbers, 100), (Products.Feta, 70), (Products.Onion, 20)),
        Dish(Dishes.Caprese, "Капрезе", "Салаты", 520m,
            (Products.Tomatoes, 150), (Products.Mozzarella, 125), (Products.Basil, 10)),
        Dish(Dishes.ArugulaShrimp, "Руккола с креветками", "Салаты", 680m,
            (Products.Arugula, 60), (Products.Shrimp, 80), (Products.Tomatoes, 60), (Products.Parmesan, 15)),
        Dish(Dishes.BeefSteak, "Стейк из вырезки с картофелем", "Горячее", 1490m,
            (Products.Beef, 250), (Products.Potatoes, 200), (Products.Butter, 20), (Products.Garlic, 5)),
        Dish(Dishes.GrilledSalmon, "Лосось на гриле со шпинатом", "Горячее", 1190m,
            (Products.Salmon, 180), (Products.Spinach, 80), (Products.Cream, 30), (Products.Lemons, 1)),
        Dish(Dishes.MushroomSoup, "Крем-суп из шампиньонов", "Супы", 420m,
            (Products.Mushrooms, 150), (Products.Cream, 100), (Products.Potatoes, 100), (Products.Onion, 30)),
        Dish(Dishes.ChickenSoup, "Куриный суп", "Супы", 350m,
            (Products.Chicken, 100), (Products.Potatoes, 100), (Products.Onion, 30)),
        Dish(Dishes.SpinachOmelette, "Омлет со шпинатом и фетой", "Завтраки", 390m,
            (Products.Eggs, 3), (Products.Spinach, 40), (Products.Feta, 40), (Products.Milk, 50)),
        Dish(Dishes.Pancakes, "Блины", "Завтраки", 320m,
            (Products.Milk, 200), (Products.Flour, 120), (Products.Eggs, 2), (Products.Butter, 20)),
        Dish(Dishes.Margherita, "Пицца Маргарита", "Пицца", 610m,
            (Products.Flour, 200), (Products.Tomatoes, 100), (Products.Mozzarella, 125), (Products.Basil, 5)),
        Dish(Dishes.MashedPotatoes, "Картофельное пюре", "Гарниры", 210m,
            (Products.Potatoes, 250), (Products.Milk, 80), (Products.Butter, 30)),
    ];

    private static Guid P(int n) => Guid.Parse($"10000000-0000-0000-0000-{n:D12}");
    private static Guid D(int n) => Guid.Parse($"20000000-0000-0000-0000-{n:D12}");

    private static IReadOnlyList<Product> WithSubstitutes(IReadOnlyList<Product> products)
    {
        foreach (var product in products)
            product.SetSubstitutes(Substitutes.Where(s => s.ProductId == product.Id).Select(s => (s.SubstituteId, s.Ratio)));
        return products;
    }

    /// <param name="price">Ориентировочная цена закупки за кг / л / шт, ₽.</param>
    private static Product Product(Guid id, string name, MeasureUnit unit, int shelfLifeHours, decimal minStock, decimal packSize,
        string category, string storage, decimal price, string supplier) => new(id, name, unit)
    {
        DefaultShelfLifeHours = shelfLifeHours,
        MinStockQty = minStock,
        PackSize = packSize,
        Category = category,
        StorageType = storage,
        ReferenceUnitCost = Prices.ToBase(price, unit),
        Supplier = supplier,
    };

    /// <summary>Гарниры и добавки: их можно убрать из блюда («без лимона», «без лука») — по просьбе гостя или когда кончились.</summary>
    public static readonly IReadOnlySet<Guid> Optional = new HashSet<Guid> { Products.Lemons, Products.Basil, Products.Parmesan, Products.Garlic, Products.Onion };

    private static Dish Dish(Guid id, string name, string category, decimal price, params (Guid ProductId, decimal Quantity)[] ingredients)
    {
        var dish = new Dish(id, name) { Category = category, Price = price };
        dish.SetIngredients(ingredients.Select(i => (i.ProductId, i.Quantity, Optional.Contains(i.ProductId))));
        return dish;
    }
}
