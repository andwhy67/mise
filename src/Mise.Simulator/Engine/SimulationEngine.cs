using Microsoft.Extensions.Logging;
using Mise.Application.Simulator;
using Mise.Simulator.Api;
using Mise.Simulator.Model;

namespace Mise.Simulator.Engine;

/// <summary>Счётчик ретраев HTTP (заполняется из resilience-пайплайна).</summary>
public sealed class RetryCounter
{
    private int _value;
    public int Value => Volatile.Read(ref _value);
    public void Increment() => Interlocked.Increment(ref _value);
}

/// <summary>
/// Модель ресторана (PLAN §8): модельное время идёт в <c>speed</c> раз быстрее реального; за тик — пуассоновский
/// поток заказов по суточной кривой, поставки по точке заказа, закрытие заказов (через 15 модельных минут) и отмены
/// (доля заказов по сценарию), «хаос» — всплески и повторы с тем же Idempotency-Key.
/// Однопоточная: тики вызывает воркер последовательно.
/// </summary>
public sealed class SimulationEngine(IMiseApi api, TimeProvider time, RetryCounter retries, ILogger<SimulationEngine> logger)
{
    public static readonly TimeSpan RestockInterval = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan CompleteAfter = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan CatalogRefresh = TimeSpan.FromMinutes(1);
    public const int MaxOrdersPerTick = 40;
    public const int MaxCompletionsPerTick = 20;
    public const int MaxRecent = 20;
    /// <summary>Доля заказов, которые гость отменяет (минимум для любого сценария; Chaos — больше).</summary>
    private const double BaseCancelProbability = 0.01;

    private readonly Queue<SimulatorActivity> _recent = new();
    private readonly List<OpenOrder> _open = [];
    private Random _random = new();
    private IReadOnlyList<ProductInfo> _products = [];
    private Dictionary<Guid, DishInfo> _dishes = [];
    private DateTimeOffset? _catalogLoadedAt;
    private DateTimeOffset? _lastRestockCheck;
    private bool _pendingFullRestock;
    private int _retriesAtStart;
    private Counters _counters = new();

    public bool Running { get; private set; }
    public SimulatorScenario Scenario { get; private set; } = SimulatorScenario.Normal;
    public int Speed { get; private set; } = 1;
    public long AppliedVersion { get; private set; } = -1;
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? SimulatedTime { get; private set; }

    /// <summary>Применяет желаемое состояние из API (идемпотентно по версии).</summary>
    public void Apply(SimulatorControl control)
    {
        if (control.Version == AppliedVersion)
            return;

        var speed = Math.Clamp(control.Speed, SimulatorProtocol.MinSpeed, SimulatorProtocol.MaxSpeed);
        if (control.Running && !Running)
        {
            var now = time.GetUtcNow();
            _random = control.Seed is { } seed ? new Random(seed) : new Random();
            _counters = new Counters();
            _retriesAtStart = retries.Value;
            _open.Clear();
            _recent.Clear();
            _lastRestockCheck = null;
            _catalogLoadedAt = null;
            _pendingFullRestock = control.Scenario == SimulatorScenario.FreshDelivery;
            StartedAt = now;
            SimulatedTime = now;
            Record("scenario", $"Старт: {control.Scenario}, ×{speed}");
        }
        else if (control.Running && control.Scenario != Scenario)
        {
            // Эффект нового сценария (например, короткие сроки Spoilage) должен быть виден сразу, а не через интервал.
            _lastRestockCheck = null;
            _pendingFullRestock = control.Scenario == SimulatorScenario.FreshDelivery;
            Record("scenario", $"Сценарий: {control.Scenario}");
        }
        else if (control.Running && speed != Speed)
        {
            Record("scenario", $"Скорость: ×{speed}");
        }
        else if (!control.Running && Running)
        {
            Record("scenario", "Остановлен");
        }

        Running = control.Running;
        Scenario = control.Scenario;
        Speed = speed;
        AppliedVersion = control.Version;
    }

    public async Task TickAsync(TimeSpan realElapsed, CancellationToken ct)
    {
        if (!Running || SimulatedTime is not { } from)
            return;

        var to = from + realElapsed * Speed;
        SimulatedTime = to;
        var profile = ScenarioProfile.For(Scenario);

        await Guard("catalog", () => EnsureCatalogAsync(ct));
        if (_dishes.Count == 0)
            return;
        await Guard("supply", () => RestockAsync(profile, to, ct));
        await Guard("order", () => PlaceOrdersAsync(profile, from, to, ct));
        await Guard("order", () => AdvanceOrdersAsync(to, ct));
    }

    public SimulatorStatus Snapshot(string instanceId) => new(
        instanceId,
        Running,
        Scenario,
        Speed,
        AppliedVersion,
        time.GetUtcNow(),
        StartedAt,
        SimulatedTime,
        new SimulatorCounters(
            _counters.OrdersCreated, _counters.OrdersRejected, _counters.OrdersReplayed, _counters.OrdersCancelled,
            _counters.OrdersCompleted, _counters.Supplies, _counters.SupplyItems, retries.Value - _retriesAtStart, _counters.Errors),
        _recent.Reverse().ToList());

    private async Task EnsureCatalogAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (_catalogLoadedAt is { } loaded && now - loaded < CatalogRefresh)
            return;
        _products = await api.GetProductsAsync(ct);
        _dishes = (await api.GetDishesAsync(ct)).ToDictionary(d => d.Id);
        _catalogLoadedAt = now;
    }

    private async Task RestockAsync(ScenarioProfile profile, DateTimeOffset simNow, CancellationToken ct)
    {
        var full = _pendingFullRestock;
        if (!full)
        {
            if (!profile.SuppliesEnabled || (_lastRestockCheck is { } last && simNow - last < RestockInterval))
                return;
        }
        _lastRestockCheck = simNow;
        _pendingFullRestock = false;

        var usable = await api.GetUsableStockAsync(ct);
        if (SupplyPlanner.Plan(_random, _products, usable, profile, time.GetUtcNow(), full) is not { } draft)
            return;

        var outcome = await api.CreateSupplyAsync(draft, Guid.NewGuid().ToString(), ct);
        switch (outcome)
        {
            case { IsSuccess: true }:
                _counters.Supplies++;
                _counters.SupplyItems += draft.Items.Count;
                var shortest = draft.Items.Where(i => i.ExpiresAt is not null).Select(i => i.ExpiresAt).Min();
                Record("supply", $"{draft.Number}: {draft.Items.Count} поз. от «{draft.Supplier}»"
                    + (shortest is { } at ? $", короткий срок до {at.ToOffset(TrafficModel.MoscowOffset):HH:mm}" : string.Empty));
                break;
            default:
                Fail("supply", $"{draft.Number}: {outcome.Kind} {outcome.Detail}");
                break;
        }
    }

    private async Task PlaceOrdersAsync(ScenarioProfile profile, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var lambda = (double)(TrafficModel.Expected(from, to) * profile.OrderRateFactor);
        var count = Poisson.Sample(_random, lambda);
        if (profile.BurstProbability > 0 && _random.NextDouble() < profile.BurstProbability)
        {
            count += profile.BurstSize;
            Record("scenario", $"Всплеск: +{profile.BurstSize} заказов");
        }

        for (var i = 0; i < Math.Min(count, MaxOrdersPerTick); i++)
        {
            var lines = OrderGenerator.Next(_random, _dishes.Values.ToList());
            if (lines.Count == 0)
                return;

            var key = Guid.NewGuid().ToString();
            var outcome = await api.CreateOrderAsync(lines, key, ct);
            Register(outcome, lines, to, profile);

            if (outcome.Kind == OutcomeKind.Created && _random.NextDouble() < profile.DuplicateProbability)
            {
                // Хаос: касса «не дождалась ответа» и повторила тот же запрос — API обязан вернуть тот же заказ.
                var replay = await api.CreateOrderAsync(lines, key, ct);
                if (replay.Kind == OutcomeKind.Replayed && replay.Id == outcome.Id)
                {
                    _counters.OrdersReplayed++;
                    Record("replay", $"Повтор {outcome.Number} с тем же Idempotency-Key — дубля нет");
                }
                else
                {
                    Fail("replay", $"Повтор {outcome.Number}: {replay.Kind} {replay.Detail}");
                }
            }
        }
    }

    private void Register(ApiOutcome outcome, IReadOnlyList<OrderLine> lines, DateTimeOffset simNow, ScenarioProfile profile)
    {
        var description = string.Join(", ", lines.Select(l => $"{DishName(l.DishId)}{(l.Quantity > 1 ? $" ×{l.Quantity}" : "")}"));
        switch (outcome.Kind)
        {
            case OutcomeKind.Created:
                _counters.OrdersCreated++;
                // Отменит ли гость заказ, решается сразу: доля отмен не зависит от частоты тиков.
                var cancel = _random.NextDouble() < Math.Max(BaseCancelProbability, profile.CancelProbability);
                _open.Add(new OpenOrder(outcome.Id!.Value, outcome.Number ?? "?", simNow, cancel));
                Record("order", $"{outcome.Number}: {description}");
                break;
            case OutcomeKind.Replayed:
                _counters.OrdersReplayed++;
                break;
            case OutcomeKind.Rejected:
                _counters.OrdersRejected++;
                Record("rejected", $"Отказ ({description}): не хватает {outcome.Detail}");
                break;
            default:
                Fail("order", $"Заказ ({description}): {outcome.Kind} {outcome.Detail}");
                break;
        }
    }

    private async Task AdvanceOrdersAsync(DateTimeOffset simNow, CancellationToken ct)
    {
        foreach (var order in _open.Where(o => o.Cancel).ToList())
        {
            _open.Remove(order);
            var outcome = await api.CancelOrderAsync(order.Id, ct);
            if (outcome.IsSuccess)
            {
                _counters.OrdersCancelled++;
                Record("cancel", $"{order.Number} отменён — продукты вернулись на склад");
            }
        }

        var due = _open.Where(o => simNow - o.At >= CompleteAfter).Take(MaxCompletionsPerTick).ToList();
        foreach (var order in due)
        {
            _open.Remove(order);
            var outcome = await api.CompleteOrderAsync(order.Id, ct);
            if (outcome.IsSuccess)
                _counters.OrdersCompleted++;
        }
    }

    private string DishName(Guid dishId) => _dishes.TryGetValue(dishId, out var dish) ? dish.Name : "?";

    private async Task Guard(string kind, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                       or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException)
        {
            Fail(kind, ex.Message);
        }
    }

    private void Fail(string kind, string message)
    {
        _counters.Errors++;
        logger.LogWarning("Simulator {Kind} failed: {Message}", kind, message);
        Record("error", message);
    }

    private void Record(string kind, string message)
    {
        _recent.Enqueue(new SimulatorActivity(time.GetUtcNow(), kind, message));
        while (_recent.Count > MaxRecent)
            _recent.Dequeue();
    }

    private sealed record OpenOrder(Guid Id, string Number, DateTimeOffset At, bool Cancel);

    private sealed class Counters
    {
        public int OrdersCreated;
        public int OrdersRejected;
        public int OrdersReplayed;
        public int OrdersCancelled;
        public int OrdersCompleted;
        public int Supplies;
        public int SupplyItems;
        public int Errors;
    }
}
