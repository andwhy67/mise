using Microsoft.Extensions.Logging.Abstractions;
using Mise.Application.Simulator;
using Mise.Simulator.Api;
using Mise.Simulator.Engine;
using Mise.Simulator.Model;

namespace Mise.Simulator.Tests;

public class SimulationEngineTests
{
    // 10:00Z = 13:00 по Москве — пик обеда.
    private static readonly DateTimeOffset Lunch = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeApi _api = new();
    private readonly ManualTime _time = new(Lunch);
    private readonly SimulationEngine _engine;

    public SimulationEngineTests() => _engine = new SimulationEngine(_api, _time, new RetryCounter(), NullLogger<SimulationEngine>.Instance);

    [Fact]
    public void Nothing_happens_until_started()
    {
        _engine.Apply(SimulatorControl.Default);

        Assert.False(_engine.Running);
        Assert.Null(_engine.SimulatedTime);
    }

    [Fact]
    public async Task Speed_compresses_time_and_lunch_produces_orders()
    {
        _engine.Apply(Control(running: true, speed: 60, seed: 1));

        await TickAsync(TimeSpan.FromSeconds(60)); // час модельного времени

        Assert.Equal(Lunch.AddHours(1), _engine.SimulatedTime);
        var status = _engine.Snapshot("test");
        Assert.InRange(status.Counters.OrdersCreated, 15, 40); // ожидание ~32 в 13:00
        Assert.Equal(1, status.Counters.Supplies);              // первая проверка запаса — сразу
        Assert.Equal(0, status.Counters.Errors);
        Assert.Contains(status.Recent, a => a.Kind == "order");
    }

    [Fact]
    public async Task Rejections_are_counted_as_normal_outcome()
    {
        _api.RejectOrders = true;
        _engine.Apply(Control(running: true, speed: 60, seed: 2));

        await TickAsync(TimeSpan.FromSeconds(30));

        var status = _engine.Snapshot("test");
        Assert.True(status.Counters.OrdersRejected > 0);
        Assert.Equal(0, status.Counters.OrdersCreated);
        Assert.Equal(0, status.Counters.Errors);
        Assert.Contains(status.Recent, a => a.Kind == "rejected" && a.Message.Contains("Бекон"));
    }

    [Fact]
    public async Task Supply_delay_brings_no_supplies()
    {
        _engine.Apply(Control(running: true, speed: 60, seed: 3, scenario: SimulatorScenario.SupplyDelay));

        for (var i = 0; i < 5; i++)
            await TickAsync(TimeSpan.FromSeconds(60));

        Assert.Empty(_api.Supplies);
        Assert.True(_api.Orders.Count > 0);
    }

    [Fact]
    public async Task Fresh_delivery_restocks_everything_once_on_switch()
    {
        _engine.Apply(Control(running: true, speed: 1, seed: 4, version: 1));
        await TickAsync(TimeSpan.FromSeconds(1));
        var before = _api.Supplies.Count;
        // Спагетти выше точки заказа (плановая поставка их бы не везла), но ниже цели; бекона с избытком.
        _api.Usable[FakeApi.Pasta.Id] = 4000;
        _api.Usable[FakeApi.Bacon.Id] = 10_000;

        _engine.Apply(Control(running: true, speed: 1, seed: 4, version: 2, scenario: SimulatorScenario.FreshDelivery));
        await TickAsync(TimeSpan.FromSeconds(1));
        await TickAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(before + 1, _api.Supplies.Count);
        var line = Assert.Single(_api.Supplies[^1].Items);
        Assert.Equal(FakeApi.Pasta.Id, line.ProductId);
        Assert.Equal(2000m, line.Quantity);
    }

    [Fact]
    public async Task Switching_to_spoilage_delivers_short_lived_stock_right_away()
    {
        _engine.Apply(Control(running: true, speed: 1, seed: 8, version: 1));
        await TickAsync(TimeSpan.FromSeconds(1));
        _api.Usable.Clear(); // склад опустел

        _engine.Apply(Control(running: true, speed: 1, seed: 8, version: 2, scenario: SimulatorScenario.Spoilage));
        await TickAsync(TimeSpan.FromSeconds(1));

        var supply = _api.Supplies[^1];
        var bacon = supply.Items.Single(i => i.ProductId == FakeApi.Bacon.Id);
        Assert.InRange(bacon.ExpiresAt!.Value, _time.GetUtcNow().AddMinutes(5), _time.GetUtcNow().AddMinutes(30));
        Assert.NotNull(supply.Items.Single(i => i.ProductId == FakeApi.Pasta.Id).ShelfLifeHours); // сухой склад — как обычно
    }

    [Fact]
    public async Task Chaos_replays_requests_with_the_same_key_and_cancels_orders()
    {
        _engine.Apply(Control(running: true, speed: 60, seed: 5, scenario: SimulatorScenario.Chaos));

        for (var i = 0; i < 6; i++)
            await TickAsync(TimeSpan.FromSeconds(60));

        var status = _engine.Snapshot("test");
        Assert.True(status.Counters.OrdersReplayed > 0);
        Assert.True(status.Counters.OrdersCancelled > 0);
        Assert.True(status.Counters.OrdersCompleted > 0);
        Assert.Equal(0, status.Counters.Errors);
        // Повтор с тем же ключом не создаёт заказ: уникальных ключей столько же, сколько созданных заказов.
        Assert.Equal(status.Counters.OrdersCreated, _api.Orders.Count);
    }

    [Fact]
    public async Task Same_control_version_is_applied_once_and_stop_halts_activity()
    {
        var start = Control(running: true, speed: 60, seed: 6, version: 3);
        _engine.Apply(start);
        await TickAsync(TimeSpan.FromSeconds(10));
        var orders = _api.Orders.Count;

        _engine.Apply(start); // повтор той же версии — без сброса счётчиков
        Assert.Equal(orders, _engine.Snapshot("t").Counters.OrdersCreated);

        _engine.Apply(start with { Running = false, Version = 4 });
        await TickAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(orders, _api.Orders.Count);
        Assert.Equal("scenario", _engine.Snapshot("t").Recent[0].Kind);
    }

    [Fact]
    public async Task Api_failures_are_counted_and_do_not_stop_the_engine()
    {
        _api.FailOrders = true;
        _engine.Apply(Control(running: true, speed: 60, seed: 7));

        await TickAsync(TimeSpan.FromSeconds(30));

        Assert.True(_engine.Snapshot("t").Counters.Errors > 0);
        Assert.True(_engine.Running);
    }

    private async Task TickAsync(TimeSpan elapsed)
    {
        _time.Advance(elapsed);
        await _engine.TickAsync(elapsed, CancellationToken.None);
    }

    private static SimulatorControl Control(bool running, int speed, int seed, long version = 1,
        SimulatorScenario scenario = SimulatorScenario.Normal) =>
        new(running, scenario, speed, seed, version, null);
}

internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan delta) => _now += delta;
}

/// <summary>API-заглушка: запоминает вызовы; повтор с тем же ключом → Replayed, как настоящий API.</summary>
internal sealed class FakeApi : IMiseApi
{
    public static readonly ProductInfo Bacon = new(Guid.NewGuid(), "Бекон", "Gram", 168, 500, 500);
    public static readonly ProductInfo Pasta = new(Guid.NewGuid(), "Спагетти", "Gram", 8760, 2000, 1000);

    private readonly List<DishInfo> _dishes =
    [
        new(Guid.NewGuid(), "Карбонара", "Паста", 690m, true, [new(Pasta.Id, 120), new(Bacon.Id, 60)]),
        new(Guid.NewGuid(), "Паста с беконом", "Паста", 590m, true, [new(Pasta.Id, 100), new(Bacon.Id, 40)]),
        new(Guid.NewGuid(), "Спагетти", "Паста", 390m, true, [new(Pasta.Id, 150)]),
    ];

    public Dictionary<Guid, decimal> Usable { get; } = [];
    public Dictionary<string, Guid> Orders { get; } = [];
    public List<SupplyDraft> Supplies { get; } = [];
    public bool RejectOrders { get; set; }
    public bool FailOrders { get; set; }

    public Task<IReadOnlyList<ProductInfo>> GetProductsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ProductInfo>>([Bacon, Pasta]);

    public Task<IReadOnlyList<DishInfo>> GetDishesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<DishInfo>>(_dishes);

    public Task<IReadOnlyDictionary<Guid, decimal>> GetUsableStockAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>(Usable));

    public Task<ApiOutcome> CreateOrderAsync(IReadOnlyList<OrderLine> lines, string idempotencyKey, CancellationToken ct)
    {
        if (FailOrders)
            throw new HttpRequestException("API недоступен");
        if (RejectOrders)
            return Task.FromResult(new ApiOutcome(OutcomeKind.Rejected, Detail: "Бекон"));
        if (Orders.TryGetValue(idempotencyKey, out var existing))
            return Task.FromResult(new ApiOutcome(OutcomeKind.Replayed, existing, "ORD"));
        var id = Guid.NewGuid();
        Orders[idempotencyKey] = id;
        return Task.FromResult(new ApiOutcome(OutcomeKind.Created, id, $"ORD-{Orders.Count:D6}"));
    }

    public Task<ApiOutcome> CreateSupplyAsync(SupplyDraft draft, string idempotencyKey, CancellationToken ct)
    {
        Supplies.Add(draft);
        foreach (var item in draft.Items)
            Usable[item.ProductId] = Usable.GetValueOrDefault(item.ProductId) + item.Quantity;
        return Task.FromResult(new ApiOutcome(OutcomeKind.Created, Guid.NewGuid(), draft.Number));
    }

    public Task<ApiOutcome> CancelOrderAsync(Guid orderId, CancellationToken ct) => Task.FromResult(new ApiOutcome(OutcomeKind.Replayed, orderId));

    public Task<ApiOutcome> CompleteOrderAsync(Guid orderId, CancellationToken ct) => Task.FromResult(new ApiOutcome(OutcomeKind.Replayed, orderId));
}
