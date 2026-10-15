using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mise.Application;
using Mise.Application.Abstractions;
using Mise.Application.Auth;
using Mise.Domain.Abstractions;
using Mise.Infrastructure;
using Mise.Infrastructure.Persistence;
using Mise.Infrastructure.Seed;
using Mise.Infrastructure.Time;
using Serilog;

namespace Mise.Api.Infrastructure;

/// <summary>
/// <c>dotnet Mise.Api.dll --seed-demo-history [--days 14] [--seed 42] [--force]</c> — миграции, стартовый словарь
/// и история демо-ресторана за N суток до текущего момента; веб-сервер и фоновые задачи не запускаются.
/// Без --force отказывается, если у демо-ресторана уже есть заказы (чтобы не задвоить историю).
/// </summary>
internal static class DemoHistoryCommand
{
    public const string Flag = "--seed-demo-history";

    public static async Task<int> RunAsync(string[] args)
    {
        var now = DateTimeOffset.UtcNow;
        var builder = WebApplication.CreateBuilder(args.Where(a => a != Flag).ToArray());
        builder.Host.UseSerilog((context, _, logger) => logger.ReadFrom.Configuration(context.Configuration).WriteTo.Console());
        builder.Services
            .AddApplication()
            .AddInfrastructure(builder.Configuration)
            .AddApiServices(builder.Configuration);
        // Часы идут по сценарию истории, действия — от имени демо-сотрудников.
        var clock = new FakeClock(now);
        builder.Services.Replace(ServiceDescriptor.Singleton<IClock>(clock));
        builder.Services.Replace(ServiceDescriptor.Singleton<ICurrentUser, ScriptedCurrentUser>());

        await using var app = builder.Build();
        var ct = CancellationToken.None;
        await app.Services.GetServices<IHostedService>().OfType<DatabaseInitializer>().Single().StartAsync(ct);

        var days = Option(args, "--days", 14);
        var seed = Option(args, "--seed", 42);
        using (app.Services.GetRequiredService<ITenant>().Use(SeedData.DemoRestaurantId))
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var hasOrders = await scope.ServiceProvider.GetRequiredService<MiseDbContext>().Orders.AnyAsync(ct);
            if (hasOrders && !args.Contains("--force"))
            {
                Log.Error("У демо-ресторана уже есть заказы — история не строится (пересоздайте БД или добавьте --force).");
                return 1;
            }
        }

        var result = await app.Services.GetRequiredService<DemoHistoryGenerator>()
            .RunAsync(new DemoHistoryOptions(SeedData.DemoRestaurantId, now, days, seed), ct);
        Log.Information("Готово: {Days} сут., поставок {Supplies}, заказов {Orders}, отказов {Refusals} (с заменой {Substituted}), " +
                        "отмен {Cancelled}, ручных списаний {WriteOffs}", days, result.Supplies, result.Orders, result.Refusals,
            result.Substituted, result.Cancelled, result.WriteOffs);
        return 0;
    }

    private static int Option(string[] args, string name, int fallback)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value) ? value : fallback;
    }
}
