using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mise.Application.Abstractions;
using Mise.Domain.Orders;
using Mise.Domain.Stock;
using Mise.Infrastructure.Persistence;
using Mise.Infrastructure.Seed;

namespace Mise.Api.IntegrationTests;

/// <summary>
/// Генератор истории идёт через настоящие сервисы — значит, история согласована: остаток не уходит в минус,
/// движения каждой партии сходятся с её остатком, заказы доходят до выдачи, деньги и расход посчитаны.
/// Строится в отдельном (новом) ресторане, чтобы не задевать демо-ресторан остальных тестов.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DemoHistoryTests(MiseApiFactory factory)
{
    [Fact]
    public async Task Two_days_of_history_are_consistent_by_the_rules_of_the_system()
    {
        var anonymous = factory.CreateClientAs(null);
        var register = await anonymous.PostAsJsonAsync("/api/v1/auth/register", new
        {
            restaurantName = "История",
            displayName = "Менеджер",
            login = TestApi.Unique("hist").ToLowerInvariant(),
            password = "Strong-Pass-123",
            fillDemoCatalog = true,
        });
        await register.EnsureStatusAsync(HttpStatusCode.OK);
        var session = await register.ReadJsonAsync();
        var restaurantId = session.GetProperty("restaurant").GetProperty("id").GetGuid();
        var end = factory.Clock.UtcNow;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<DemoHistoryGenerator>()
                .RunAsync(new DemoHistoryOptions(restaurantId, end, Days: 2, Seed: 7, OrdersPerDay: 60), CancellationToken.None);
            Assert.True(result.Orders > 60, $"заказов {result.Orders}");
            Assert.True(result.Supplies >= 6, $"поставок {result.Supplies}");
        }
        Assert.Equal(end, factory.Clock.UtcNow);

        await using (var scope = factory.Services.CreateAsyncScope())
        using (scope.ServiceProvider.GetRequiredService<ITenant>().Use(restaurantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<MiseDbContext>();
            var batches = await db.Batches.AsNoTracking().ToListAsync();
            var sums = await db.StockMovements.AsNoTracking().GroupBy(m => m.BatchId)
                .Select(g => new { BatchId = g.Key, Sum = g.Sum(m => m.QtyDelta) }).ToDictionaryAsync(x => x.BatchId, x => x.Sum);
            Assert.NotEmpty(batches);
            Assert.All(batches, b => Assert.True(b.RemainingQty >= 0));
            Assert.All(batches, b => Assert.Equal(b.RemainingQty, sums.GetValueOrDefault(b.Id)));
            Assert.True(await db.Orders.CountAsync(o => o.Status == OrderStatus.Served) > 30);
            Assert.True(await db.ConsumptionHours.AnyAsync());
            Assert.True(await db.StockMovements.AnyAsync(m => m.Type == MovementType.ExpiryWriteOff || m.Type == MovementType.ManualWriteOff));
        }

        var client = factory.CreateClientAs(null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        var money = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/analytics/money?from={Uri.EscapeDataString(end.AddDays(-3).ToString("O"))}&to={Uri.EscapeDataString(end.AddMinutes(1).ToString("O"))}");
        Assert.True(money.GetProperty("purchases").GetDecimal() > 0);
        Assert.True(money.GetProperty("costOfSales").GetDecimal() > 0);
        Assert.True(money.GetProperty("revenue").GetDecimal() > money.GetProperty("costOfSales").GetDecimal());
    }
}
