using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mise.Application.Abstractions;
using Mise.Application.Audit;
using Mise.Application.Common;
using Mise.Application.Expiry;
using Mise.Application.Forecasting;
using Mise.Application.HotOffers;
using Mise.Application.Menu;
using Mise.Application.Orders;
using Mise.Application.Stock;
using Mise.Application.Supplies;
using Mise.Domain.Abstractions;
using Mise.Domain.Catalog;
using Mise.Domain.Common;
using Mise.Domain.Stock;
using Mise.Domain.Users;
using Mise.Infrastructure.Time;

namespace Mise.Infrastructure.Seed;

/// <param name="Days">Сколько суток истории до <see cref="End"/>.</param>
/// <param name="End">«Сейчас» — момент, на котором история заканчивается (часы остаются на нём).</param>
/// <param name="Seed">Одинаковый seed — одинаковая история.</param>
public sealed record DemoHistoryOptions(Guid RestaurantId, DateTimeOffset End, int Days = 14, int Seed = 42, double OrdersPerDay = 100);

public sealed record DemoHistoryResult(int Supplies, int Orders, int Refusals, int Substituted, int Cancelled, int WriteOffs, int ExpiryTicks);

/// <summary>
/// История ресторана за N суток — через те же сервисы, что работают в API, на «ускоренных» часах (FakeClock):
/// поставки по графику поставщиков, заказы с обеденным и вечерним пиком, статусы до выдачи, отмены, отказы 422
/// (часть — с заменой по согласию), ручные и автоматические списания. Поэтому всё согласовано по правилам системы:
/// FEFO, движения ↔ остатки, стоп-лист с причинами, деньги по ценам партий, почасовой расход для прогнозов.
/// Детерминирован (seed). Работает только в указанном ресторане.
/// </summary>
public sealed class DemoHistoryGenerator(IServiceScopeFactory scopes, IClock clock, ITenant tenant, ILogger<DemoHistoryGenerator> logger)
{
    private static readonly TimeSpan Moscow = TimeSpan.FromHours(3);

    /// <summary>Поставщик → раз во сколько дней привозит, со сдвигом (чтобы мясо и рыба приходили в разные дни) и час приезда.</summary>
    private static readonly Dictionary<string, (int Every, int Offset, int Hour)> Schedule = new()
    {
        ["Молочный двор"] = (1, 0, 8),
        ["ООО «Свежесть»"] = (1, 0, 8),
        ["Агроферма «Зелёная»"] = (1, 0, 9),
        ["Мясной цех"] = (2, 0, 9),
        ["Рыбный двор"] = (2, 1, 9),
        ["Сухой склад №1"] = (7, 0, 10),
    };

    /// <summary>Доля заказов по часам открытия (11–22): обед и ужин.</summary>
    private static readonly double[] HourWeights = [0.05, 0.11, 0.13, 0.10, 0.05, 0.04, 0.05, 0.09, 0.12, 0.12, 0.09, 0.05];

    private static readonly string[] DefectReasons = ["Брак при приёмке", "Упало при подаче", "Не прошло проверку качества", "Повреждена упаковка"];

    private readonly FakeClock _clock = clock as FakeClock ?? throw new InvalidOperationException("История строится на управляемых часах (FakeClock).");

    private readonly PriorityQueue<Func<CancellationToken, Task>, (DateTimeOffset At, long Seq)> _events = new();
    private long _seq;
    private Random _rng = new(42);
    private DemoHistoryOptions _options = null!;
    private List<Product> _products = [];
    private List<Dish> _dishes = [];
    private Dictionary<Guid, double> _dishWeights = [];
    private Dictionary<Guid, decimal> _dailyUsage = [];
    private Dictionary<UserRole, (Guid Id, string Login)> _actors = [];
    private HashSet<Guid> _stoppedDishes = [];
    private List<Guid> _hotDishes = [];
    private int _supplies, _orders, _refusals, _substituted, _cancelled, _writeOffs, _ticks;

    public async Task<DemoHistoryResult> RunAsync(DemoHistoryOptions options, CancellationToken ct)
    {
        _options = options;
        _rng = new Random(options.Seed);
        var start = MoscowDayStart(options.End.AddDays(-options.Days)).AddHours(7);
        _clock.Set(start);
        using var restaurant = tenant.Use(options.RestaurantId);
        await LoadCatalogAsync(ct);

        for (var day = MoscowDayStart(start); day < options.End; day = day.AddDays(1))
            PlanDay(day, (int)Math.Round((day - MoscowDayStart(start)).TotalDays));
        PlanFinale();

        while (_events.TryDequeue(out var action, out var key))
        {
            if (key.At > options.End)
                break;
            if (key.At > _clock.UtcNow)
                _clock.Set(key.At);
            try
            {
                await action(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Отдельное событие (заказ на исчерпанный продукт, статус уже сменён) не должно ломать историю.
                logger.LogDebug(ex, "History event at {At} skipped", key.At);
            }
        }

        _clock.Set(options.End);
        await AcknowledgeOldAlertsAsync(options.End.AddHours(-24), ct);
        await WithScopeAsync(s => s.GetRequiredService<IConsumptionAggregator>().RefreshAsync(ct));
        await WithScopeAsync(s => s.GetRequiredService<StopListService>().RefreshAsync(ct));
        var result = new DemoHistoryResult(_supplies, _orders, _refusals, _substituted, _cancelled, _writeOffs, _ticks);
        logger.LogInformation("Demo history: {Result}", result);
        return result;
    }

    // ---------- План ----------

    private void PlanDay(DateTimeOffset day, int index)
    {
        // Монитор сроков и пересчёт расхода — каждый час, как фоновые задачи.
        for (var hour = 0; hour < 24; hour++)
            Plan(day.AddHours(hour).AddMinutes(1), ExpiryTickAsync);

        foreach (var (supplier, (every, offset, hour)) in Schedule)
            if ((index + offset) % every == 0)
                Plan(day.AddHours(hour).AddMinutes(_rng.Next(0, 50)), ct => DeliverAsync(supplier, ct));

        var weekday = day.ToOffset(Moscow).DayOfWeek;
        var factor = weekday switch
        {
            DayOfWeek.Friday => 1.25,
            DayOfWeek.Saturday => 1.35,
            DayOfWeek.Sunday => 1.2,
            DayOfWeek.Monday => 0.85,
            _ => 1.0,
        };
        var count = (int)Math.Round(_options.OrdersPerDay * factor * (0.9 + _rng.NextDouble() * 0.2));
        for (var i = 0; i < count; i++)
        {
            var hour = 11 + Pick(HourWeights);
            Plan(day.AddHours(hour).AddSeconds(_rng.Next(0, 3600)), PlaceOrderAsync);
        }

        // Ручные списания — пару-тройку раз в неделю; заявка поставщику — раз в три дня.
        if (_rng.NextDouble() < 0.4)
            Plan(day.AddHours(16).AddMinutes(_rng.Next(0, 120)), ManualWriteOffAsync);
        if (index % 3 == 1)
            Plan(day.AddHours(15).AddMinutes(_rng.Next(0, 60)), SupplierRequestAsync);
    }

    /// <summary>
    /// К «сейчас» на экранах должно быть что показать: креветки два дня не везли (блюда в стопе — «Что докупить»,
    /// «С заменой»), а лосось, сливки и шпинат пришли с коротким сроком (горячие предложения, «спасено»).
    /// </summary>
    private void PlanFinale()
    {
        // Партии с коротким сроком привезли за двое суток: к «сейчас» им осталось 12–22 ч, а запаса — больше,
        // чем уйдёт по обычному спросу. Без продвижения часть пропадёт — это и показывают горячие предложения.
        Plan(_options.End.AddHours(-48), async ct =>
        {
            var lines = new[] { ("Лосось охл.", 18), ("Сливки 33%", 22), ("Шпинат", 12) }
                .Select(x => (Product: _products.FirstOrDefault(p => p.Name == x.Item1), HoursLeft: x.Item2))
                .Where(x => x.Product is not null)
                .Select(x => (x.Product!.Supplier, Line: Line(x.Product, RoundToPack(x.Product, _dailyUsage.GetValueOrDefault(x.Product.Id) * 4.5m),
                    _options.End.AddHours(x.HoursLeft))));
            // Каждый продукт — от своего поставщика.
            foreach (var group in lines.GroupBy(x => x.Supplier))
                await ReceiveAsync(group.Key ?? "—", group.Select(x => x.Line).ToList(), ct);
        });

        // Последние полчаса — свежие заказы: на экране кухни они ещё в работе (их статусы — уже после «сейчас»).
        foreach (var minutesAgo in new[] { 26, 19, 13, 8, 4, 1 })
            Plan(_options.End.AddMinutes(-minutesAgo), PlaceOrderAsync);
    }

    private bool Withheld(Product product) =>
        product.Name == "Креветки очищенные" && _clock.UtcNow >= _options.End.AddHours(-44);

    // ---------- События ----------

    private async Task DeliverAsync(string supplier, CancellationToken ct)
    {
        var (every, _, _) = Schedule[supplier];
        var cover = every + 0.6m;
        var usable = await WithScopeAsync(s => MenuSnapshot(s, ct));
        var lines = new List<CreateSupplyItemRequest>();
        foreach (var product in _products.Where(p => p.Supplier == supplier && !Withheld(p)))
        {
            var target = _dailyUsage.GetValueOrDefault(product.Id) * cover + product.MinStockQty;
            var need = target - usable.GetValueOrDefault(product.Id);
            if (need <= 0)
                continue;
            // Иногда поставщик недовозит — отсюда дефициты, отказы и стоп-лист в истории.
            var shortfall = _rng.NextDouble() < 0.08 ? 0.5m : 1m;
            var quantity = RoundToPack(product, need * shortfall * Jitter(0.85, 1.15));
            if (quantity <= 0)
                continue;
            // Часть строк — с датой «годен до» на этикетке поставщика (короче нормы).
            DateTimeOffset? expires = product.DefaultShelfLifeHours is { } hours && _rng.NextDouble() < 0.25
                ? _clock.UtcNow.AddHours(Math.Max(6, hours * (0.7 + _rng.NextDouble() * 0.25)))
                : null;
            lines.Add(Line(product, quantity, expires));
        }
        if (lines.Count > 0)
            await ReceiveAsync(supplier, lines, ct);
    }

    private CreateSupplyItemRequest Line(Product product, decimal quantity, DateTimeOffset? expiresAt)
    {
        // Цена партии — справочная ±10 %, до копеек за кг / л / шт.
        decimal? price = product.ReferenceUnitCost is { } reference
            ? Prices.ToBase(decimal.Round(Prices.ToDisplay(reference, product.Unit) * Jitter(0.92, 1.1), 0), product.Unit)
            : null;
        var shelfLife = expiresAt is null ? product.DefaultShelfLifeHours ?? 72 : (int?)null;
        return new CreateSupplyItemRequest(product.Id, null, quantity, BaseInput(product.Unit), shelfLife, expiresAt, price);
    }

    private async Task ReceiveAsync(string supplier, IReadOnlyList<CreateSupplyItemRequest> lines, CancellationToken ct)
    {
        var number = $"SUP-{_clock.UtcNow.ToOffset(Moscow):yyyyMMdd-HHmm}-{++_supplies:D3}";
        var result = await WithScopeAsync(s => s.GetRequiredService<SupplyService>()
            .CreateAsync(new CreateSupplyRequest(null, number, _clock.UtcNow, supplier, lines), Guid.NewGuid().ToString(), ct));
        await AuditAsync(UserRole.Warehouse, "supply.create", "Supply", result.Supply.Id.ToString(), ct);
        await RefreshMenuAsync(ct);
    }

    private async Task PlaceOrderAsync(CancellationToken ct)
    {
        var request = new CreateOrderRequest(PickDishes());
        if (request.Items.Count == 0)
            return;
        var placed = await TryOrderAsync(request, retry: true, ct);
        if (placed is null)
            return;
        _orders++;
        await AuditAsync(UserRole.Chef, "order.create", "Order", placed.Value.ToString(), ct);

        // Дальше — повар ведёт заказ до выдачи; 2 % отменяют (гость передумал, ошибка официанта).
        var at = _clock.UtcNow;
        if (_rng.NextDouble() < 0.02)
        {
            Plan(at.AddMinutes(_rng.Next(1, 5)), async token =>
            {
                await WithScopeAsync(s => s.GetRequiredService<OrderService>().CancelAsync(placed.Value, token));
                _cancelled++;
                await AuditAsync(UserRole.Chef, "order.cancel", "Order", placed.Value.ToString(), token);
            });
            return;
        }
        var cooking = at.AddMinutes(_rng.Next(1, 6));
        var done = cooking.AddMinutes(_rng.Next(8, 23));
        var served = done.AddMinutes(_rng.Next(1, 6));
        Plan(cooking, token => ChangeStatusAsync(placed.Value, Mise.Domain.Orders.OrderStatus.Cooking, token));
        Plan(done, token => ChangeStatusAsync(placed.Value, Mise.Domain.Orders.OrderStatus.Done, token));
        Plan(served, token => ChangeStatusAsync(placed.Value, Mise.Domain.Orders.OrderStatus.Served, token));
    }

    /// <summary>
    /// Отказ 422 — как в зале: если заменитель есть, повар чаще соглашается на замену; иначе убирают то, чего нет,
    /// или гость уходит. Отказ попадает в отчёт «чего не хватает» в любом случае.
    /// </summary>
    private async Task<Guid?> TryOrderAsync(CreateOrderRequest request, bool retry, CancellationToken ct)
    {
        try
        {
            var result = await WithScopeAsync(s => s.GetRequiredService<OrderService>().CreateAsync(request, Guid.NewGuid().ToString(), ct));
            return result.Order.Id;
        }
        catch (BusinessRuleException ex) when (ex.Code == OrderService.Errors.InsufficientStock && retry)
        {
            _refusals++;
            var missing = ex.Details.GetValueOrDefault("missing") as IReadOnlyList<MissingIngredientDto> ?? [];
            var substitutions = missing
                .Select(m => (m.ProductId, Offer: m.Substitutes.FirstOrDefault(o => o.Enough)))
                .Where(x => x.Offer is not null)
                .Select(x => new OrderSubstitutionRequest(x.ProductId, x.Offer!.ProductId))
                .ToList();
            if (substitutions.Count == missing.Count && missing.Count > 0 && _rng.NextDouble() < 0.7)
            {
                var placed = await TryOrderAsync(request with { Substitutions = substitutions }, retry: false, ct);
                if (placed is not null)
                    _substituted++;
                return placed;
            }
            if (_rng.NextDouble() < 0.5)
            {
                var short1 = missing.SelectMany(m => m.Dishes).ToHashSet();
                var rest = request.Items.Where(i => !short1.Contains(_dishes.First(d => d.Id == i.DishId).Name)).ToList();
                if (rest.Count > 0)
                    return await TryOrderAsync(new CreateOrderRequest(rest), retry: false, ct);
            }
            return null;
        }
        catch (BusinessRuleException ex) when (ex.Code == OrderService.Errors.InsufficientStock)
        {
            _refusals++;
            return null;
        }
    }

    private async Task ChangeStatusAsync(Guid orderId, Mise.Domain.Orders.OrderStatus status, CancellationToken ct)
    {
        await WithScopeAsync(s => s.GetRequiredService<OrderService>().ChangeStatusAsync(orderId, new ChangeOrderStatusRequest(status), ct));
        await AuditAsync(UserRole.Chef, "order.status", "Order", orderId.ToString(), ct);
    }

    private async Task ExpiryTickAsync(CancellationToken ct)
    {
        _ticks++;
        await WithScopeAsync(s => s.GetRequiredService<ExpiryMonitorService>().RunOnceAsync(ct));
        await WithScopeAsync(s => s.GetRequiredService<IConsumptionAggregator>().RefreshAsync(ct));
        if (_clock.UtcNow.ToOffset(Moscow).Hour % 2 == 0)
            await RefreshMenuAsync(ct);
    }

    private async Task ManualWriteOffAsync(CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var batch = await WithScopeAsync(s => s.GetRequiredService<IMiseDbContext>().Batches.AsNoTracking()
            .Where(b => b.Status == BatchStatus.Active && b.RemainingQty > 0 && b.ExpiresAt > now)
            .OrderBy(b => b.Id).Skip(_rng.Next(0, 20)).FirstOrDefaultAsync(ct));
        if (batch is null)
            return;
        var product = _products.First(p => p.Id == batch.ProductId);
        var quantity = Math.Max(1m, decimal.Round(batch.RemainingQty * Jitter(0.05, 0.15), product.Unit == MeasureUnit.Piece ? 0 : 0));
        await WithScopeAsync(s => s.GetRequiredService<WriteOffService>()
            .WriteOffAsync(batch.Id, new WriteOffRequest(quantity, BaseInput(product.Unit), DefectReasons[_rng.Next(DefectReasons.Length)]), ct));
        _writeOffs++;
        await AuditAsync(UserRole.Manager, "batch.write_off", "Batch", batch.Id.ToString(), ct);
    }

    private async Task SupplierRequestAsync(CancellationToken ct)
    {
        var suggestions = await WithScopeAsync(s => s.GetRequiredService<ForecastService>().GetSupplySuggestionsAsync(null, ct));
        var group = suggestions.Items
            .GroupBy(i => _products.First(p => p.Id == i.ProductId).Supplier)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        if (group is null)
            return;
        Act(UserRole.Warehouse);
        await WithScopeAsync(s => s.GetRequiredService<SupplyRequestService>().CreateAsync(
            new CreateSupplyRequestRequest(group.Key, group.Select(i => new SupplyRequestLineRequest(i.ProductId, i.SuggestedQty)).ToList()), ct));
    }

    /// <summary>Смена реагировала на уведомления: старше суток — прочитаны, в колокольчике только свежее.</summary>
    private async Task AcknowledgeOldAlertsAsync(DateTimeOffset before, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IMiseDbContext>();
        var old = await db.Alerts.Where(a => a.AcknowledgedAt == null && a.CreatedAt < before).ToListAsync(ct);
        foreach (var alert in old)
            alert.Acknowledge(alert.CreatedAt.AddMinutes(_rng.Next(5, 90)));
        await db.SaveChangesAsync(ct);
    }

    // ---------- Выбор блюд ----------

    private IReadOnlyList<CreateOrderItemRequest> PickDishes()
    {
        var count = 1 + Pick([0.45, 0.35, 0.15, 0.05]);
        var available = _dishes.Where(d => !_stoppedDishes.Contains(d.Id)).ToList();
        if (available.Count == 0)
            return [];
        var chosen = new List<Guid>();
        // Официант предлагает «горящие» блюда: в трети заказов одно из них — так в отчётах появляется «спасено».
        if (_hotDishes.Count > 0 && _rng.NextDouble() < 0.33)
            chosen.Add(_hotDishes[_rng.Next(Math.Min(3, _hotDishes.Count))]);
        while (chosen.Count < count)
        {
            var pick = available[Pick(available.Select(d => _dishWeights[d.Id]).ToArray())].Id;
            if (!chosen.Contains(pick))
                chosen.Add(pick);
            else if (chosen.Count >= available.Count)
                break;
        }
        return chosen.Select(id => new CreateOrderItemRequest(id, _rng.NextDouble() < 0.82 ? 1 : 2)).ToList();
    }

    private async Task RefreshMenuAsync(CancellationToken ct)
    {
        var menu = await WithScopeAsync(s => s.GetRequiredService<MenuQueryService>().GetAvailabilityAsync(ct));
        _stoppedDishes = menu.Where(d => d.InStopList || d.MaxPortions <= 0).Select(d => d.DishId).ToHashSet();
        var hot = await WithScopeAsync(s => s.GetRequiredService<HotOfferService>().GetAsync(5, null, ct));
        _hotDishes = hot.Offers.Select(o => o.DishId).ToList();
    }

    // ---------- Подготовка ----------

    private async Task LoadCatalogAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IMiseDbContext>();
        _products = await db.Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);
        _dishes = await db.Dishes.AsNoTracking().Include(d => d.Ingredients).Where(d => d.IsActive).OrderBy(d => d.Name).ToListAsync(ct);
        if (_products.Count == 0 || _dishes.Count == 0)
            throw new InvalidOperationException("В ресторане нет справочника — историю строить не из чего.");

        // Популярность блюд: у каждого своя, но паста и салаты — хиты.
        _dishWeights = _dishes.ToDictionary(d => d.Id, d => 0.6 + _rng.NextDouble() * 1.4 + (d.Category is "Паста" or "Салаты" ? 0.8 : 0));
        var totalWeight = _dishWeights.Values.Sum();
        var portionsPerDay = _options.OrdersPerDay * 1.8 * 1.18;
        _dailyUsage = [];
        foreach (var dish in _dishes)
        {
            var portions = (decimal)(portionsPerDay * _dishWeights[dish.Id] / totalWeight);
            foreach (var ingredient in dish.Ingredients)
                _dailyUsage[ingredient.ProductId] = _dailyUsage.GetValueOrDefault(ingredient.ProductId) + portions * ingredient.Quantity;
        }

        _actors = (await db.Users.AsNoTracking().Where(u => u.IsActive).ToListAsync(ct))
            .GroupBy(u => u.Role).ToDictionary(g => g.Key, g => (g.First().Id, g.First().Login));
    }

    // ---------- Помощники ----------

    private void Plan(DateTimeOffset at, Func<CancellationToken, Task> action) => _events.Enqueue(action, (at, ++_seq));

    private int Pick(IReadOnlyList<double> weights)
    {
        var roll = _rng.NextDouble() * weights.Sum();
        for (var i = 0; i < weights.Count; i++)
            if ((roll -= weights[i]) <= 0)
                return i;
        return weights.Count - 1;
    }

    private decimal Jitter(double from, double to) => (decimal)(from + _rng.NextDouble() * (to - from));

    private static decimal RoundToPack(Product product, decimal quantity)
    {
        var pack = product.PackSize > 0 ? product.PackSize : 1m;
        return Math.Ceiling(quantity / pack) * pack;
    }

    private static InputUnit BaseInput(MeasureUnit unit) => unit switch
    {
        MeasureUnit.Gram => InputUnit.Gram,
        MeasureUnit.Milliliter => InputUnit.Milliliter,
        _ => InputUnit.Piece,
    };

    private static DateTimeOffset MoscowDayStart(DateTimeOffset instant)
    {
        var local = instant.ToOffset(Moscow);
        return new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, Moscow).ToUniversalTime();
    }

    private static async Task<Dictionary<Guid, decimal>> MenuSnapshot(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<IMiseDbContext>();
        var now = services.GetRequiredService<IClock>().UtcNow;
        return await db.Batches.AsNoTracking()
            .Where(b => b.Status == BatchStatus.Active && b.RemainingQty > 0 && b.ExpiresAt > now)
            .GroupBy(b => b.ProductId)
            .Select(g => new { g.Key, Qty = g.Sum(b => b.RemainingQty) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
    }

    /// <summary>Действия — от имени демо-сотрудника роли (для журнала), если такой есть в ресторане.</summary>
    private void Act(UserRole role)
    {
        if (ScriptedCurrentUser.Instance is { } user && (_actors.TryGetValue(role, out var actor) || _actors.TryGetValue(UserRole.Manager, out actor)))
            user.Set(actor.Id, actor.Login, role);
    }

    private async Task AuditAsync(UserRole role, string action, string entity, string entityId, CancellationToken ct)
    {
        if (!_actors.TryGetValue(role, out var actor) && !_actors.TryGetValue(UserRole.Manager, out actor))
            return;
        await WithScopeAsync(s => s.GetRequiredService<AuditService>().RecordAsync(action, actor.Id, actor.Login, entity, entityId, null, ct));
    }

    private async Task WithScopeAsync(Func<IServiceProvider, Task> work)
    {
        await using var scope = scopes.CreateAsyncScope();
        await work(scope.ServiceProvider);
    }

    private async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }
}
