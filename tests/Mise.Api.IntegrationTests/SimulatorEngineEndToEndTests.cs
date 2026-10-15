extern alias simulator;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Mise.Infrastructure.Persistence;
using simulator::Mise.Simulator.Api;
using simulator::Mise.Simulator.Engine;
// Протокол подключён в симулятор ссылкой на файл — его типы в сборке симулятора берём через alias.
using SimControl = simulator::Mise.Application.Simulator.SimulatorControl;
using SimScenario = simulator::Mise.Application.Simulator.SimulatorScenario;

namespace Mise.Api.IntegrationTests;

/// <summary>Сквозной прогон: настоящий движок симулятора ходит в настоящий API (как процесс симулятора в compose).</summary>
[Collection(ApiCollection.Name)]
public sealed class SimulatorEngineEndToEndTests(MiseApiFactory factory)
{
    [Fact]
    public async Task Engine_drives_real_api_without_negative_stock_or_duplicate_orders()
    {
        var api = new MiseApiClient(factory.CreateClient());
        var time = new SteppedTime(factory.Clock.UtcNow);
        var engine = new SimulationEngine(api, time, new RetryCounter(), NullLogger<SimulationEngine>.Instance);
        var ordersBefore = await CountOrdersAsync();

        engine.Apply(new SimControl(true, SimScenario.Chaos, 60, Seed: 7, Version: 100, UpdatedAt: null));
        for (var i = 0; i < 8; i++)
        {
            var elapsed = TimeSpan.FromSeconds(30); // 30 модельных минут на ×60
            time.Advance(elapsed);
            await engine.TickAsync(elapsed, CancellationToken.None);
        }

        var status = engine.Snapshot("e2e");
        var counters = status.Counters;
        var errors = string.Join("; ", status.Recent.Where(a => a.Kind == "error").Select(a => a.Message));
        Assert.True(counters.Errors == 0, errors);
        Assert.True(counters.Supplies > 0, "движок должен завезти продукты");
        Assert.True(counters.OrdersCreated > 20, $"создано заказов: {counters.OrdersCreated}");
        Assert.True(counters.OrdersReplayed > 0, "в Chaos есть повторы с тем же ключом");
        // Повторы с тем же Idempotency-Key не создают новых заказов.
        Assert.Equal(counters.OrdersCreated, await CountOrdersAsync() - ordersBefore);
        await AssertLedgerConsistentAsync();
    }

    private async Task<int> CountOrdersAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        using var demo = factory.AsDemoRestaurant();
        return await scope.ServiceProvider.GetRequiredService<MiseDbContext>().Orders.CountAsync();
    }

    private async Task AssertLedgerConsistentAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        using var demo = factory.AsDemoRestaurant();
        var db = scope.ServiceProvider.GetRequiredService<MiseDbContext>();
        var batches = await db.Batches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.RemainingQty);
        var ledger = await db.StockMovements.AsNoTracking()
            .GroupBy(m => m.BatchId).Select(g => new { g.Key, Sum = g.Sum(m => m.QtyDelta) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum);

        Assert.All(batches, b =>
        {
            Assert.True(b.Value >= 0, $"Отрицательный остаток в партии {b.Key}");
            Assert.Equal(ledger[b.Key], b.Value);
        });
    }

    private sealed class SteppedTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }
}
