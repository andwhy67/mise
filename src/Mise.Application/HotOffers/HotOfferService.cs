using System.Collections.Concurrent;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mise.Application.Abstractions;
using Mise.Application.Common;
using Mise.Domain.Abstractions;
using Mise.Domain.HotOffers;
using Mise.Domain.Menu;
using Mise.Domain.Stock;

namespace Mise.Application.HotOffers;

/// <summary>
/// Память между пересчётами (singleton): какие блюда показаны сейчас (для гистерезиса) и что ушло клиентам последним.
/// Своя на каждый инстанс API — у границы порога инстансы могут кратко расходиться (docs/assumptions.md).
/// </summary>
public sealed class HotOfferState
{
    private readonly ConcurrentDictionary<string, IReadOnlyCollection<Guid>> _shown = new();
    private string? _lastPublished;
    private readonly Lock _gate = new();

    public IReadOnlyCollection<Guid> Shown(string scorer) => _shown.GetValueOrDefault(scorer) ?? [];

    public void Remember(string scorer, IEnumerable<Guid> dishIds) => _shown[scorer] = dishIds.ToHashSet();

    /// <summary>true — подпись выдачи изменилась с последней публикации (и запоминается как опубликованная).</summary>
    public bool TryMarkPublished(string signature)
    {
        lock (_gate)
        {
            if (signature == _lastPublished)
                return false;
            _lastPublished = signature;
            return true;
        }
    }
}

/// <summary>Горячие предложения (PLAN §4.4): «что продавать в первую очередь» с объяснением.</summary>
public sealed class HotOfferService(
    IMiseDbContext db,
    IClock clock,
    IEnumerable<IHotOfferScorer> scorers,
    HotOfferState state,
    IOptions<HotOfferOptions> options)
{
    public const int MaxLimit = 20;

    public async Task<HotOffersDto> GetAsync(int? limit, string? scorerVersion, CancellationToken ct)
    {
        var settings = options.Value;
        var parameters = settings.ToParameters();
        var scorer = ResolveScorer(scorerVersion ?? settings.Scorer);
        if (limit is < 1 or > MaxLimit)
            throw new[] { new ValidationFailure("limit", $"limit должен быть от 1 до {MaxLimit}.") }.ToValidationException();

        var now = clock.UtcNow;
        var (input, dishes) = await LoadInputAsync(now, settings.HistoryDays, ct);
        var scored = scorer.Score(input, parameters);

        var previous = state.Shown(scorer.Version);
        var selected = HotOfferSelector.Select(scored, previous, parameters);
        state.Remember(scorer.Version, selected.Select(o => o.DishId));
        if (limit is { } requested && requested != parameters.TopN)
            selected = HotOfferSelector.Select(scored, previous, parameters with { TopN = requested });

        return new HotOffersDto(now, scorer.Version, selected.Select((o, i) => ToDto(i + 1, o, dishes[o.DishId])).ToList());
    }

    /// <summary>Выдача по умолчанию, если она изменилась с последней публикации (состав, порядок или score на 1+); иначе null.</summary>
    public async Task<HotOffersDto?> GetIfChangedAsync(CancellationToken ct)
    {
        var offers = await GetAsync(null, null, ct);
        var signature = offers.Scorer + "|" + string.Join(";", offers.Offers.Select(o => $"{o.DishId:N}:{decimal.Round(o.Score):0}"));
        return state.TryMarkPublished(signature) ? offers : null;
    }

    private IHotOfferScorer ResolveScorer(string version) =>
        scorers.FirstOrDefault(s => s.Version == version)
        ?? throw new[] { new ValidationFailure("scorer", $"Неизвестная версия алгоритма «{version}». Доступны: {string.Join(", ", scorers.Select(s => s.Version))}.") }
            .ToValidationException();

    private async Task<(HotOfferInput Input, Dictionary<Guid, Domain.Catalog.Dish> Dishes)> LoadInputAsync(
        DateTimeOffset now, int historyDays, CancellationToken ct)
    {
        var batches = await db.Batches.AsNoTracking()
            .Where(b => b.Status == BatchStatus.Active && b.RemainingQty > 0 && b.ExpiresAt > now)
            .Select(b => new HotOfferBatch(b.Id, b.ProductId, b.ReceivedAt, b.ExpiresAt, b.RemainingQty))
            .ToListAsync(ct);
        var usable = batches.GroupBy(b => b.ProductId).ToDictionary(g => g.Key, g => g.Sum(b => b.RemainingQty));
        var consumption = await ConsumptionHistory.DailyAsync(db, now, historyDays, ct);

        var products = await db.Products.AsNoTracking().ToListAsync(ct);
        var dishes = await db.Dishes.AsNoTracking().Include(d => d.Ingredients).ToListAsync(ct);

        var input = new HotOfferInput(
            products.Select(p => new HotOfferProduct(p.Id, p.Name, p.Unit, p.DefaultShelfLifeHours, p.MinStockQty,
                consumption.GetValueOrDefault(p.Id))).ToList(),
            batches,
            dishes.Select(d =>
            {
                var availability = MenuAvailability.Calculate(d, usable);
                // Блюдо в стопе (в т.ч. выключенное вручную) не рекомендуется.
                return new HotOfferDish(d.Id, d.Name, availability.InStopList ? 0 : availability.MaxPortions,
                    d.Ingredients.Select(i => new HotOfferIngredient(i.ProductId, i.Quantity)).ToList());
            }).ToList(),
            now);
        return (input, dishes.ToDictionary(d => d.Id));
    }

    private static HotOfferDto ToDto(int rank, HotOffer offer, Domain.Catalog.Dish dish)
    {
        var text = HotOfferExplainer.Explain(offer);
        return new HotOfferDto(rank, offer.DishId, offer.DishName, dish.Category, dish.Price, offer.MaxPortions, offer.Score,
            text.Headline, text.Explanation,
            offer.Reasons.Select((r, i) => new HotOfferReasonDto(
                r.ProductId, r.ProductName, r.Unit, r.AtRiskQty, r.UnsoldQty, r.HoursLeft, r.ExpiresAt, r.QtyPerPortion,
                r.PortionsBurned, r.Pressure, r.Share, r.Contribution, text.Reasons[i])).ToList());
    }
}

/// <summary>Расход по истории движений: чистое списание под заказы (минус возвраты при отмене).</summary>
public static class ConsumptionHistory
{
    /// <summary>
    /// Среднесуточный расход по продуктам за последние <paramref name="days"/> дней. Делитель — длительность истории
    /// (от первого движения в окне), но не меньше суток: несколько минут работы не экстраполируются в огромную скорость.
    /// </summary>
    public static async Task<Dictionary<Guid, decimal>> DailyAsync(IMiseDbContext db, DateTimeOffset now, int days, CancellationToken ct)
    {
        var since = now.AddDays(-days);
        var rows = await db.StockMovements.AsNoTracking()
            .Where(m => m.CreatedAt >= since && m.CreatedAt <= now
                && (m.Type == MovementType.OrderConsumption || m.Type == MovementType.OrderCancelReturn))
            .GroupBy(m => m.ProductId)
            .Select(g => new { ProductId = g.Key, Net = -g.Sum(m => m.QtyDelta), First = g.Min(m => m.CreatedAt) })
            .ToListAsync(ct);
        if (rows.Count == 0)
            return [];

        var span = now - rows.Min(r => r.First);
        var spanDays = (decimal)Math.Clamp(span.TotalDays, 1d, days);
        return rows.Where(r => r.Net > 0).ToDictionary(r => r.ProductId, r => decimal.Round(r.Net / spanDays, 3));
    }
}
