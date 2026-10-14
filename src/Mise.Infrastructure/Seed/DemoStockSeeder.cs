using Mise.Application.Common;
using Mise.Application.Supplies;
using Mise.Domain.Abstractions;
using static Mise.Infrastructure.Seed.SeedData.Products;

namespace Mise.Infrastructure.Seed;

/// <param name="Created">Номера поставок, принятых сейчас.</param>
/// <param name="Replayed">Номера поставок, уже принятых сегодня (повторный вызов).</param>
public sealed record DemoSeedResult(IReadOnlyList<string> Created, IReadOnlyList<string> Replayed);

/// <summary>
/// Демо-наполнение склада «одной кнопкой» (PLAN §8, §11.11): обычные поставки через <see cref="SupplyService"/> —
/// настоящие партии и движения. Подобрано так, чтобы демо сразу было интересным:
/// лосось, сливки и шпинат близки к истечению (материал для hot offers), бекона — на 2 карбонары,
/// креветок нет (оба блюда с креветками в стопе), остальное — с запасом.
/// Номера поставок содержат дату: повтор в тот же день идемпотентен (тот же Idempotency-Key → 200, без дублей).
/// </summary>
public sealed class DemoStockSeeder(SupplyService supplies, IClock clock)
{
    public async Task<DemoSeedResult> SeedAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var prefix = $"DEMO-{now:yyyyMMdd}";
        var created = new List<string>();
        var replayed = new List<string>();

        foreach (var request in BuildSupplies(prefix, now))
        {
            try
            {
                var result = await supplies.CreateAsync(request, idempotencyKey: request.Number, ct);
                (result.Created ? created : replayed).Add(request.Number);
            }
            catch (ConflictException ex) when (ex.Code is SupplyService.Errors.IdempotencyKeyReused or SupplyService.Errors.DuplicateNumber)
            {
                // Сегодняшняя поставка уже принята: тело отличается только временем (оно считается от «сейчас») — это повтор.
                replayed.Add(request.Number);
            }
        }

        return new DemoSeedResult(created, replayed);
    }

    /// <summary>Поставки демо-набора (открыто для тестов: тот же набор проверяется скорером горячих предложений).</summary>
    public static IEnumerable<CreateSupplyRequest> BuildSupplies(string prefix, DateTimeOffset now)
    {
        // Основной запас: свежий, срок — по умолчанию из словаря.
        yield return new CreateSupplyRequest(
            Id: null,
            Number: $"{prefix}-01",
            Supplier: "ООО «Свежесть» (демо)",
            CreatedAt: now,
            Items:
            [
                Item(Spaghetti, 5000), Item(Arborio, 3000), Item(Flour, 5000), Item(Potatoes, 10000), Item(Onion, 3000),
                Item(Garlic, 600), Item(Lemons, 20), Item(Eggs, 90), Item(Milk, 6000), Item(Butter, 2000),
                Item(Parmesan, 1500), Item(Mozzarella, 2500), Item(Feta, 1000), Item(Chicken, 4000), Item(Beef, 3000),
                Item(Tomatoes, 5000), Item(Cucumbers, 3000), Item(Romaine, 2000), Item(Arugula, 800), Item(Basil, 200),
                Item(Mushrooms, 2500),
            ]);

        // Поставка двухсуточной давности: срок на исходе — Warning/Critical сразу после старта.
        yield return new CreateSupplyRequest(
            Id: null,
            Number: $"{prefix}-02",
            Supplier: "Рыбный двор (демо)",
            CreatedAt: now.AddHours(-60),
            Items:
            [
                Item(Salmon, 2000, shelfLifeHours: 72), // осталось 12 ч (17%) — Warning
                Item(Cream, 3000, shelfLifeHours: 80),  // осталось 20 ч (25%) — Warning
                Item(Spinach, 600, shelfLifeHours: 64), // осталось 4 ч (6%) — Critical
            ]);

        // Бекона — ровно на 2 порции карбонары: пара заказов, и блюдо уходит в стоп.
        yield return new CreateSupplyRequest(
            Id: null,
            Number: $"{prefix}-03",
            Supplier: "Мясной цех (демо)",
            CreatedAt: now,
            Items: [Item(Bacon, 130)]);
    }

    private static CreateSupplyItemRequest Item(Guid productId, decimal quantity, int? shelfLifeHours = null) =>
        new(productId, Name: null, Quantity: quantity, Unit: null, ShelfLifeHours: shelfLifeHours, ExpiresAt: null);
}
