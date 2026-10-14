using Microsoft.AspNetCore.Mvc;
using Mise.Application.Simulator;
using Mise.Infrastructure.Seed;

namespace Mise.Api.Endpoints;

internal static class SimulatorEndpoints
{
    public static RouteGroupBuilder MapSimulatorEndpoints(this RouteGroupBuilder api)
    {
        var simulator = api.MapGroup("/simulator").WithTags("Симулятор");

        simulator.MapGet("/state", (SimulatorService service, CancellationToken ct) => service.GetStateAsync(ct))
            .WithSummary("Желаемое и фактическое состояние симулятора, счётчики, последние события")
            .WithDescription("online = симулятор присылает heartbeat; control.version растёт при каждом изменении, " +
                "status.appliedVersion показывает, какую версию симулятор уже применил.");

        simulator.MapPost("/start", (
                [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] StartSimulatorRequest? request,
                SimulatorService service,
                CancellationToken ct) => service.StartAsync(request ?? new StartSimulatorRequest(), ct))
            .WithSummary("Запустить симулятор (необязательно: scenario, speed 1–60, seed для воспроизводимости)");

        simulator.MapPost("/stop", (SimulatorService service, CancellationToken ct) => service.StopAsync(ct))
            .WithSummary("Остановить симулятор");

        simulator.MapPost("/scenario", (SetScenarioRequest request, SimulatorService service, CancellationToken ct) =>
                service.SetScenarioAsync(request, ct))
            .WithSummary("Сценарий: Normal, RushHour, SupplyDelay, Spoilage, FreshDelivery, Chaos");

        simulator.MapPost("/speed", (SetSpeedRequest request, SimulatorService service, CancellationToken ct) =>
                service.SetSpeedAsync(request, ct))
            .WithSummary("Скорость модельного времени ×1…×60");

        return api;
    }

    /// <summary>Демо-наполнение склада; маршрут есть только при Demo:Enabled = true.</summary>
    public static RouteGroupBuilder MapDemoEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/demo/seed-stock", (DemoStockSeeder seeder, CancellationToken ct) => seeder.SeedAsync(ct))
            .WithTags("Демо")
            .WithSummary("Наполнить склад демо-поставками (повтор в тот же день идемпотентен)")
            .WithDescription("Лосось, сливки и шпинат близки к истечению, бекона — на 2 карбонары, креветок нет.");
        return api;
    }
}
