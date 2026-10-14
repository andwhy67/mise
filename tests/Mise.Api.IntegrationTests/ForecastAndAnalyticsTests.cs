using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mise.Application.Abstractions;
using Mise.Domain.Forecasting;
using Mise.Infrastructure.Persistence;

namespace Mise.Api.IntegrationTests;

/// <summary>consumption_hourly, прогноз стоп-листа, предложения поставок и аналитика на FakeClock.</summary>
[Collection(ApiCollection.Name)]
public sealed class ForecastAndAnalyticsTests(MiseApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Aggregator_sums_net_consumption_per_hour_and_is_idempotent()
    {
        var product = await _client.CreateProductAsync();
        await _client.ReceiveAsync(product, 10_000m, shelfLifeHours: 500);
        var dish = await _client.CreateDishAsync((product, 100m));
        var firstHour = ConsumptionRateEstimator.FloorToHour(factory.Clock.UtcNow);

        await _client.CreateOrderAsync((dish, 2));
        var cancelled = await _client.CreateOrderAsync((dish, 1));
        await (await _client.PostAsync($"/api/v1/orders/{cancelled.GetProperty("id").GetGuid()}/cancel", null))
            .EnsureStatusAsync(HttpStatusCode.OK);
        factory.Clock.Advance(TimeSpan.FromHours(1));
        await _client.CreateOrderAsync((dish, 3));

        await RefreshAggregateAsync();
        await RefreshAggregateAsync();

        var buckets = await BucketsAsync(product);
        Assert.Equal(new Dictionary<DateTimeOffset, decimal> { [firstHour] = 200m, [firstHour.AddHours(1)] = 300m }, buckets);
    }

    [Fact]
    public async Task Stop_list_forecast_predicts_consumption_and_expiry()
    {
        var bacon = await _client.CreateProductAsync(defaultShelfLifeHours: 500);
        var cream = await _client.CreateProductAsync(defaultShelfLifeHours: 500);
        await _client.ReceiveAsync(bacon, 5000m, shelfLifeHours: 500);
        var carbonara = await _client.CreateDishAsync((bacon, 60m));
        // Сутки истории: порция в час → ≈ 60 г/ч.
        for (var hour = 0; hour < 24; hour++)
        {
            await _client.CreateOrderAsync((carbonara, 1));
            factory.Clock.Advance(TimeSpan.FromHours(1));
        }
        var left = await _client.UsableAsync(bacon); // 5000 − 24 × 60 = 3560
        var excess = await _client.PostAsJsonAsync($"/api/v1/batches/{await FirstBatchAsync(bacon)}/write-off",
            new { quantity = left - 600m, reason = "Подготовка сценария" });
        await excess.EnsureStatusAsync(HttpStatusCode.OK);
        // Сливки не расходуются, но их единственная партия истекает через 5 ч.
        await _client.ReceiveAsync(cream, 300m, shelfLifeHours: 5);
        var sauce = await _client.CreateDishAsync((cream, 100m));

        var forecast = await _client.GetFromJsonAsync<JsonElement>("/api/v1/stoplist/forecast?horizonHours=24");

        var items = forecast.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("dishId").GetGuid());
        var byConsumption = items[carbonara];
        Assert.Equal("Consumption", byConsumption.GetProperty("cause").GetString());
        Assert.Equal(bacon, byConsumption.GetProperty("limitingProduct").GetProperty("productId").GetGuid());
        // 600 г при ≈ 50–60 г/ч → меньше порции через 9–11 ч.
        Assert.InRange(byConsumption.GetProperty("hoursLeft").GetDecimal(), 8m, 13m);
        Assert.Contains("расходуется", byConsumption.GetProperty("explanation").GetString());

        var byExpiry = items[sauce];
        Assert.Equal("Expiry", byExpiry.GetProperty("cause").GetString());
        Assert.Equal(5m, byExpiry.GetProperty("hoursLeft").GetDecimal());
        Assert.Equal(24, forecast.GetProperty("horizonHours").GetInt32());

        await (await _client.GetAsync("/api/v1/stoplist/forecast?horizonHours=0")).EnsureStatusAsync(HttpStatusCode.BadRequest);
        await (await _client.GetAsync("/api/v1/stoplist/forecast?horizonHours=500")).EnsureStatusAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Supply_suggestions_top_up_below_minimum_and_skip_well_stocked()
    {
        var low = await _client.CreateProductAsync(defaultShelfLifeHours: 200, minStockQty: 1000m);
        var plenty = await _client.CreateProductAsync(defaultShelfLifeHours: 200, minStockQty: 100m);
        await SetPackSizeAsync(low, 500m);
        await _client.ReceiveAsync(low, 300m, shelfLifeHours: 200);
        await _client.ReceiveAsync(plenty, 5000m, shelfLifeHours: 200);

        var suggestions = await _client.GetFromJsonAsync<JsonElement>("/api/v1/supply-suggestions");

        var items = suggestions.GetProperty("items").EnumerateArray().ToList();
        Assert.DoesNotContain(items, i => i.GetProperty("productId").GetGuid() == plenty);
        var item = items.Single(i => i.GetProperty("productId").GetGuid() == low);
        Assert.Equal(1000m, item.GetProperty("suggestedQty").GetDecimal()); // нужно 700 → 2 упаковки по 500
        Assert.Equal("High", item.GetProperty("urgency").GetString());
        Assert.Equal("Low", item.GetProperty("confidence").GetString());
        var reasons = item.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Contains(reasons, r => r!.StartsWith("ниже минимального остатка"));
        Assert.Contains(reasons, r => r!.StartsWith("нет истории расхода"));
        Assert.StartsWith("Заказать 1 кг:", item.GetProperty("explanation").GetString());
        Assert.Equal(72, suggestions.GetProperty("horizonHours").GetInt32());
    }

    [Fact]
    public async Task Write_off_report_splits_expiry_and_manual()
    {
        var from = factory.Clock.UtcNow;
        var product = await _client.CreateProductAsync();
        var batch = await _client.ReceiveAsync(product, 300m, shelfLifeHours: 1);
        var manual = await _client.PostAsJsonAsync($"/api/v1/batches/{batch}/write-off", new { quantity = 100m, reason = "Бой" });
        await manual.EnsureStatusAsync(HttpStatusCode.OK);
        factory.Clock.Advance(TimeSpan.FromHours(1));
        await factory.RunExpiryTickAsync();

        var report = await GetAsync("/api/v1/analytics/writeoffs", from, factory.Clock.UtcNow.AddMinutes(1));

        var row = report.GetProperty("byProduct").EnumerateArray().Single(p => p.GetProperty("productId").GetGuid() == product);
        Assert.Equal(200m, row.GetProperty("expiryQty").GetDecimal());
        Assert.Equal(100m, row.GetProperty("manualQty").GetDecimal());
        Assert.Equal(2, row.GetProperty("operations").GetInt32());
        var items = report.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("productId").GetGuid() == product).ToList();
        Assert.Equal(["ExpiryWriteOff", "ManualWriteOff"], items.Select(i => i.GetProperty("reason").GetString()).Order());
        Assert.True(report.GetProperty("expiryOperations").GetInt32() >= 1);
    }

    [Fact]
    public async Task Deficit_report_counts_stop_time_caused_by_the_product()
    {
        var from = factory.Clock.UtcNow;
        var product = await _client.CreateProductAsync();
        await _client.ReceiveAsync(product, 100m, shelfLifeHours: 200);
        var dish = await _client.CreateDishAsync((product, 100m));
        await _client.CreateOrderAsync((dish, 1)); // блюдо уходит в стоп
        factory.Clock.Advance(TimeSpan.FromMinutes(30));
        await _client.ReceiveAsync(product, 500m, shelfLifeHours: 200); // и возвращается

        var report = await GetAsync("/api/v1/analytics/deficit", from, factory.Clock.UtcNow.AddMinutes(1));

        var item = report.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("productId").GetGuid() == product);
        Assert.Equal(1, item.GetProperty("stopEvents").GetInt32());
        Assert.Equal(1, item.GetProperty("dishesAffected").GetInt32());
        Assert.Equal(30m, item.GetProperty("dishMinutesInStop").GetDecimal());
    }

    [Fact]
    public async Task Deficit_report_counts_orders_refused_because_of_the_product()
    {
        var from = factory.Clock.UtcNow;
        var scarce = await _client.CreateProductAsync();
        var plenty = await _client.CreateProductAsync();
        await _client.ReceiveAsync(scarce, 100m, shelfLifeHours: 200);
        await _client.ReceiveAsync(plenty, 5000m, shelfLifeHours: 200);
        var dish = await _client.CreateDishAsync((scarce, 60m), (plenty, 10m));

        // 2 порции × 60 г при остатке 100 г — отказ 422 дважды; со склада ничего не списано.
        await (await _client.PostOrderAsync(null, (dish, 2))).EnsureStatusAsync(HttpStatusCode.UnprocessableEntity);
        await (await _client.PostOrderAsync(null, (dish, 3))).EnsureStatusAsync(HttpStatusCode.UnprocessableEntity);
        Assert.Equal(100m, await _client.UsableAsync(scarce));

        var report = await GetAsync("/api/v1/analytics/deficit", from, factory.Clock.UtcNow.AddMinutes(1));
        var items = report.GetProperty("items").EnumerateArray().ToList();
        var item = items.Single(i => i.GetProperty("productId").GetGuid() == scarce);
        Assert.Equal(2, item.GetProperty("refusals").GetInt32());
        Assert.Equal(100m, item.GetProperty("shortageQty").GetDecimal()); // (120 − 100) + (180 − 100)
        Assert.DoesNotContain(items, i => i.GetProperty("productId").GetGuid() == plenty);
    }

    [Fact]
    public async Task Consumption_report_and_kpi()
    {
        var from = factory.Clock.UtcNow;
        var product = await _client.CreateProductAsync();
        await _client.ReceiveAsync(product, 1000m, shelfLifeHours: 200);
        var dish = await _client.CreateDishAsync((product, 50m));
        await _client.CreateOrderAsync((dish, 2));
        factory.Clock.Advance(TimeSpan.FromHours(1));
        await _client.CreateOrderAsync((dish, 1));
        var to = factory.Clock.UtcNow.AddMinutes(1);

        var hourly = await GetAsync($"/api/v1/analytics/consumption?productId={product}&bucket=Hour", from, to);
        var series = hourly.GetProperty("series").EnumerateArray().Single();
        Assert.Equal(150m, series.GetProperty("total").GetDecimal());
        Assert.Equal([100m, 50m], series.GetProperty("points").EnumerateArray().Select(p => p.GetProperty("qty").GetDecimal()));
        var daily = await GetAsync($"/api/v1/analytics/consumption?productId={product}&bucket=Day", from, to);
        Assert.Equal(150m, daily.GetProperty("series")[0].GetProperty("points").EnumerateArray().Sum(p => p.GetProperty("qty").GetDecimal()));

        var kpi = await GetAsync("/api/v1/analytics/kpi", from, to);
        var period = kpi.GetProperty("period");
        Assert.True(period.GetProperty("orders").GetInt32() >= 2);
        Assert.True(period.GetProperty("revenue").GetDecimal() >= 1500m); // 3 порции × 500
        var now = kpi.GetProperty("now");
        Assert.True(now.GetProperty("ordersToday").GetInt32() >= 0);
        Assert.True(now.GetProperty("dishesInStop").GetInt32() >= 0);

        var reversed = await _client.GetAsync($"/api/v1/analytics/kpi?from={Q(to)}&to={Q(from)}");
        await reversed.EnsureStatusAsync(HttpStatusCode.BadRequest);
    }

    private async Task<JsonElement> GetAsync(string path, DateTimeOffset from, DateTimeOffset to)
    {
        var separator = path.Contains('?') ? '&' : '?';
        return await _client.GetFromJsonAsync<JsonElement>($"{path}{separator}from={Q(from)}&to={Q(to)}");
    }

    private static string Q(DateTimeOffset value) => Uri.EscapeDataString(value.ToString("O"));

    private async Task RefreshAggregateAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IConsumptionAggregator>().RefreshAsync(CancellationToken.None);
    }

    private async Task<Dictionary<DateTimeOffset, decimal>> BucketsAsync(Guid productId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MiseDbContext>();
        return await db.ConsumptionHours.AsNoTracking().Where(c => c.ProductId == productId)
            .ToDictionaryAsync(c => c.HourBucket, c => c.QtyConsumed);
    }

    private async Task<Guid> FirstBatchAsync(Guid productId) =>
        (await _client.GetFromJsonAsync<JsonElement>($"/api/v1/stock/{productId}/batches"))
            .GetProperty("batches")[0].GetProperty("id").GetGuid();

    private async Task SetPackSizeAsync(Guid productId, decimal packSize)
    {
        var product = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/products/{productId}");
        var response = await _client.PutAsJsonAsync($"/api/v1/products/{productId}", new
        {
            name = product.GetProperty("name").GetString(),
            unit = product.GetProperty("unit").GetString(),
            defaultShelfLifeHours = product.GetProperty("defaultShelfLifeHours").GetInt32(),
            minStockQty = product.GetProperty("minStockQty").GetDecimal(),
            packSize,
            category = "Тест",
        });
        await response.EnsureStatusAsync(HttpStatusCode.OK);
    }
}
