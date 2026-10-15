using Microsoft.EntityFrameworkCore;
using Mise.Application.Abstractions;
using Mise.Application.Common;
using Mise.Domain.Abstractions;
using Mise.Domain.Catalog;
using Mise.Domain.Menu;
using Mise.Domain.Stock;

namespace Mise.Application.Menu;

/// <summary>Доступность блюд и стоп-лист — считаются вживую от пригодного остатка на текущий момент IClock.</summary>
public sealed class MenuQueryService(IMiseDbContext db, IClock clock)
{
    public async Task<IReadOnlyList<DishAvailabilityDto>> GetAvailabilityAsync(CancellationToken ct)
    {
        var snapshot = await MenuSnapshot.LoadAsync(db, clock.UtcNow, ct);
        return snapshot.Dishes
            .Select(d => snapshot.ToDto(d, MenuAvailability.Calculate(d, snapshot.Usable)))
            .OrderBy(d => d.InStopList).ThenBy(d => d.DishName)
            .ToList();
    }

    /// <summary>Доступность блюд, в составе которых есть указанные продукты, плюс явно указанные блюда (дельта).</summary>
    public async Task<IReadOnlyList<DishAvailabilityDto>> GetAvailabilityForAsync(
        IReadOnlyCollection<Guid> productIds, IReadOnlyCollection<Guid> dishIds, CancellationToken ct)
    {
        if (productIds.Count == 0 && dishIds.Count == 0)
            return [];
        var snapshot = await MenuSnapshot.LoadAsync(db, clock.UtcNow, ct);
        return snapshot.Dishes
            .Where(d => dishIds.Contains(d.Id) || d.Ingredients.Any(i => productIds.Contains(i.ProductId)))
            .Select(d => snapshot.ToDto(d, MenuAvailability.Calculate(d, snapshot.Usable)))
            .OrderBy(d => d.DishName)
            .ToList();
    }

    public async Task<IReadOnlyList<StopListItemDto>> GetStopListAsync(CancellationToken ct)
    {
        var snapshot = await MenuSnapshot.LoadAsync(db, clock.UtcNow, ct);
        var openSince = await db.StopListEntries.AsNoTracking()
            .Where(e => e.EndedAt == null)
            .ToDictionaryAsync(e => e.DishId, e => e.StartedAt, ct);

        return snapshot.Dishes
            .Select(d => (Dish: d, Availability: MenuAvailability.Calculate(d, snapshot.Usable)))
            .Where(x => x.Availability.InStopList)
            .Select(x =>
            {
                var dto = snapshot.ToDto(x.Dish, x.Availability);
                return new StopListItemDto(
                    dto.DishId, dto.DishName, dto.Category,
                    x.Availability.Status == DishAvailabilityStatus.StopManual ? StopReason.Manual : StopReason.Shortage,
                    x.Availability.Status == DishAvailabilityStatus.StopShortage ? dto.LimitingProduct : null,
                    openSince.TryGetValue(dto.DishId, out var since) ? since : null,
                    dto.Alternative);
            })
            .OrderBy(s => s.DishName)
            .ToList();
    }

    public async Task<PagedResult<StopListHistoryItemDto>> GetHistoryAsync(Guid? dishId, PageRequest page, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var query = db.StopListEntries.AsNoTracking();
        if (dishId is { } id)
            query = query.Where(e => e.DishId == id);

        var total = await query.CountAsync(ct);
        var rows = await (
                from e in query
                join d in db.Dishes on e.DishId equals d.Id
                join p in db.Products on e.LimitingProductId equals p.Id into limiting
                from p in limiting.DefaultIfEmpty()
                orderby e.StartedAt descending, e.Id descending
                select new { Entry = e, DishName = d.Name, ProductName = p == null ? null : p.Name })
            .Skip(page.Skip).Take(page.SafePageSize)
            .ToListAsync(ct);

        var items = rows.Select(r => new StopListHistoryItemDto(
                r.Entry.Id, r.Entry.DishId, r.DishName, r.Entry.Reason, r.Entry.LimitingProductId, r.ProductName,
                r.Entry.StartedAt, r.Entry.EndedAt,
                decimal.Round((decimal)((r.Entry.EndedAt ?? now) - r.Entry.StartedAt).TotalMinutes, 1)))
            .ToList();
        return new PagedResult<StopListHistoryItemDto>(items, total, page.SafePage, page.SafePageSize);
    }
}

/// <summary>Блюда с составом, словарь продуктов и пригодный остаток по продуктам на момент now.</summary>
internal sealed record MenuSnapshot(
    IReadOnlyList<Dish> Dishes,
    IReadOnlyDictionary<Guid, Product> Products,
    IReadOnlyDictionary<Guid, decimal> Usable)
{
    public static async Task<MenuSnapshot> LoadAsync(IMiseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var dishes = await db.Dishes.AsNoTracking().Include(d => d.Ingredients).ToListAsync(ct);
        var products = await db.Products.AsNoTracking().Include(p => p.Substitutes).ToDictionaryAsync(p => p.Id, ct);
        var usable = await LoadUsableAsync(db, now, ct);
        return new MenuSnapshot(dishes, products, usable);
    }

    /// <summary>usable(p) = Σ remaining по Active-партиям с expires_at &gt; now (CLAUDE.md, правило 2).</summary>
    public static async Task<Dictionary<Guid, decimal>> LoadUsableAsync(IMiseDbContext db, DateTimeOffset now, CancellationToken ct) =>
        await db.Batches.AsNoTracking()
            .Where(b => b.Status == BatchStatus.Active && b.RemainingQty > 0 && b.ExpiresAt > now)
            .GroupBy(b => b.ProductId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(b => b.RemainingQty) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Qty, ct);

    /// <summary>Правила замен из справочника: продукт → чем заменить.</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<SubstituteRule>> Rules => _rules ??= Products.Values.ToDictionary(
        p => p.Id, p => (IReadOnlyList<SubstituteRule>)p.Substitutes.Select(s => new SubstituteRule(s.SubstituteId, s.Ratio)).ToList());

    private IReadOnlyDictionary<Guid, IReadOnlyList<SubstituteRule>>? _rules;

    public DishAvailabilityDto ToDto(Dish dish, DishAvailability availability)
    {
        var removable = dish.Ingredients.Where(i => i.Removable).Select(i => i.ProductId).ToHashSet();
        var ingredients = availability.Ingredients.Select(i => ToDto(i, removable.Contains(i.ProductId))).ToList();
        // Блюдо в стопе из-за нехватки — можно ли подать с изменениями (без гарнира, с заменой).
        var alternative = availability.Status == DishAvailabilityStatus.StopShortage ? MenuAvailability.Alternative(dish, Usable, Rules) : null;
        return new DishAvailabilityDto(
            dish.Id, dish.Name, dish.Category, dish.Price, dish.IsActive,
            availability.MaxPortions, availability.Status, availability.InStopList,
            ingredients.FirstOrDefault(i => i.ProductId == availability.LimitingProductId),
            ingredients,
            alternative is null ? null : new DishAlternativeDto(alternative.Portions, alternative.Changes.Select(ToDto).ToList()));
    }

    public ModificationDto ToDto(Mise.Domain.Orders.ItemModification change) => new(
        change.ProductId, NameOf(change.ProductId), change.Kind, change.SubstituteId,
        change.SubstituteId is { } id ? NameOf(id) : null, change.Ratio);

    private string NameOf(Guid productId) => Products.GetValueOrDefault(productId)?.Name ?? "?";

    private IngredientAvailabilityDto ToDto(IngredientAvailability i, bool removable)
    {
        var product = Products.GetValueOrDefault(i.ProductId);
        var options = (product?.Substitutes ?? [])
            .Where(s => Products.ContainsKey(s.SubstituteId))
            .Select(s => new SubstituteOptionDto(s.SubstituteId, Products[s.SubstituteId].Name, Products[s.SubstituteId].Unit, s.Ratio,
                Usable.GetValueOrDefault(s.SubstituteId)))
            .OrderByDescending(o => o.Usable).ThenBy(o => o.ProductName)
            .ToList();
        return new IngredientAvailabilityDto(i.ProductId, product?.Name ?? "?", product?.Unit ?? default, i.QtyPerPortion, i.Usable, i.Portions,
            removable, options);
    }
}
