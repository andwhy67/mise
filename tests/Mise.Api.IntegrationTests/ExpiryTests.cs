using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mise.Domain.Stock;
using Mise.Infrastructure.Persistence;

namespace Mise.Api.IntegrationTests;

/// <summary>
/// Мониторинг сроков на FakeClock: пороги, дубликаты, автосписание, стоп-лист после просрочки, LowStock, ручное списание.
/// Время общее для коллекции и только растёт — каждый тест создаёт свои продукты и партии.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ExpiryTests(MiseApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Thresholds_raise_warning_then_critical_once_each()
    {
        var product = await _client.CreateProductAsync();
        var batch = await _client.ReceiveAsync(product, 100m, shelfLifeHours: 48);

        await factory.RunExpiryTickAsync();
        Assert.Empty(await BatchAlertsAsync(batch));

        factory.Clock.Advance(TimeSpan.FromHours(25)); // осталось 23 ч из 48: ≤50% и ≤24 ч
        await factory.RunExpiryTickAsync();
        await factory.RunExpiryTickAsync();
        var warning = Assert.Single(await BatchAlertsAsync(batch));
        Assert.Equal("ExpiringSoon", warning.GetProperty("type").GetString());
        Assert.Equal("Warning", warning.GetProperty("severity").GetString());
        Assert.Equal(product, warning.GetProperty("productId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(warning.GetProperty("productName").GetString()));

        factory.Clock.Advance(TimeSpan.FromHours(20)); // осталось 3 ч: ≤20% и ≤6 ч
        await factory.RunExpiryTickAsync();
        await factory.RunExpiryTickAsync();
        var alerts = await BatchAlertsAsync(batch);
        Assert.Equal(["Critical", "Warning"], alerts.Select(a => a.GetProperty("severity").GetString()));
        Assert.All(alerts, a => Assert.True(a.GetProperty("isActive").GetBoolean()));
    }

    [Fact]
    public async Task Tick_that_skipped_warning_raises_only_critical()
    {
        var product = await _client.CreateProductAsync();
        var batch = await _client.ReceiveAsync(product, 100m, shelfLifeHours: 10);

        factory.Clock.Advance(TimeSpan.FromHours(9));
        await factory.RunExpiryTickAsync();

        var alert = Assert.Single(await BatchAlertsAsync(batch));
        Assert.Equal("Critical", alert.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task Expired_batch_is_written_off_and_dish_goes_to_stop_list_with_history()
    {
        var product = await _client.CreateProductAsync();
        var batch = await _client.ReceiveAsync(product, 250m, shelfLifeHours: 2);
        var dish = await _client.CreateDishAsync((product, 100m));
        factory.Clock.Advance(TimeSpan.FromMinutes(90)); // Warning: 30 мин из 2 ч (25%)
        await factory.RunExpiryTickAsync();

        factory.Clock.Advance(TimeSpan.FromMinutes(30));
        var expiredAt = factory.Clock.UtcNow;
        var result = await factory.RunExpiryTickAsync();

        Assert.False(result.Skipped);
        Assert.Contains(dish, result.StopList.Added);

        var stored = await LoadBatchAsync(batch);
        Assert.Equal(BatchStatus.WrittenOff, stored.Status);
        Assert.Equal(0m, stored.RemainingQty);
        var movement = Assert.Single(await MovementsAsync(batch), m => m.Type == MovementType.ExpiryWriteOff);
        Assert.Equal(-250m, movement.QtyDelta);
        Assert.Equal(expiredAt, movement.CreatedAt);
        await AssertLedgerConsistentAsync(batch);
        Assert.Equal(0m, await _client.UsableAsync(product));

        var alerts = await BatchAlertsAsync(batch);
        var expired = Assert.Single(alerts, a => a.GetProperty("type").GetString() == "Expired");
        Assert.Equal("Warning", expired.GetProperty("severity").GetString()); // уже списано — информирование о потере
        var critical = Assert.Single(alerts, a => a.GetProperty("type").GetString() == "ExpiringSoon");
        Assert.False(critical.GetProperty("isActive").GetBoolean()); // партии больше нет — «скоро истечёт» закрыт

        var history = (await _client.GetFromJsonAsync<JsonElement>($"/api/v1/stoplist/history?dishId={dish}"))
            .GetProperty("items").EnumerateArray().ToList();
        var entry = Assert.Single(history);
        Assert.Equal("Shortage", entry.GetProperty("reason").GetString());
        Assert.Equal(product, entry.GetProperty("limitingProductId").GetGuid());
        Assert.Equal(expiredAt, entry.GetProperty("startedAt").GetDateTimeOffset());

        var stopOn = Assert.Single(await AlertsAsync($"dishId={dish}"));
        Assert.Equal("StopListOn", stopOn.GetProperty("type").GetString());

        // Повторный тик ничего не дублирует.
        await factory.RunExpiryTickAsync();
        Assert.Single(await MovementsAsync(batch), m => m.Type == MovementType.ExpiryWriteOff);
        Assert.Equal(2, (await BatchAlertsAsync(batch)).Count);

        // Новая поставка выводит блюдо из стопа.
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await _client.ReceiveAsync(product, 500m);
        var dishAlerts = await AlertsAsync($"dishId={dish}");
        Assert.Equal(["StopListOff", "StopListOn"], dishAlerts.Select(a => a.GetProperty("type").GetString()));
    }

    [Fact]
    public async Task Without_auto_write_off_batch_stays_expired_until_manual_write_off()
    {
        var product = await _client.CreateProductAsync();
        var batch = await _client.ReceiveAsync(product, 400m, shelfLifeHours: 1);
        factory.Clock.Advance(TimeSpan.FromHours(1));

        await factory.RunExpiryTickAsync(autoWriteOff: false);
        await factory.RunExpiryTickAsync(autoWriteOff: false);

        var stored = await LoadBatchAsync(batch);
        Assert.Equal(BatchStatus.Expired, stored.Status);
        Assert.Equal(400m, stored.RemainingQty);
        Assert.DoesNotContain(await MovementsAsync(batch), m => m.Type != MovementType.Receipt);
        var expired = Assert.Single(await BatchAlertsAsync(batch));
        Assert.Equal("Expired", expired.GetProperty("type").GetString());
        Assert.Equal("Critical", expired.GetProperty("severity").GetString()); // лежит на складе — нужно действие

        var stock = await _client.GetStockItemAsync(product);
        Assert.Equal(0m, stock.GetProperty("usableQty").GetDecimal());
        Assert.Equal(400m, stock.GetProperty("expiredQty").GetDecimal());

        var response = await WriteOffAsync(batch, new { reason = "Утилизация просрочки" });
        await response.EnsureStatusAsync(HttpStatusCode.OK);
        var body = await response.ReadJsonAsync();
        Assert.Equal(400m, body.GetProperty("writtenOffQty").GetDecimal());
        Assert.Equal("WrittenOff", body.GetProperty("status").GetString());
        var manual = Assert.Single(await MovementsAsync(batch), m => m.Type == MovementType.ManualWriteOff);
        Assert.Equal("Утилизация просрочки", manual.Reason);
        await AssertLedgerConsistentAsync(batch);
    }

    [Fact]
    public async Task Batch_received_already_expired_is_written_off_by_tick()
    {
        var product = await _client.CreateProductAsync();
        var batch = await _client.ReceiveAsync(product, 50m, shelfLifeHours: 5, createdAt: factory.Clock.UtcNow.AddHours(-6));
        Assert.Equal(BatchStatus.Expired, (await LoadBatchAsync(batch)).Status);

        await factory.RunExpiryTickAsync();

        Assert.Equal(BatchStatus.WrittenOff, (await LoadBatchAsync(batch)).Status);
        Assert.Equal("Expired", Assert.Single(await BatchAlertsAsync(batch)).GetProperty("type").GetString());
        await AssertLedgerConsistentAsync(batch);
    }

    [Fact]
    public async Task Low_stock_alert_opens_once_and_resolves_after_restock()
    {
        var product = await _client.CreateProductAsync(minStockQty: 500m);
        await _client.ReceiveAsync(product, 300m, shelfLifeHours: 200);

        await factory.RunExpiryTickAsync();
        await factory.RunExpiryTickAsync();
        var low = Assert.Single(await AlertsAsync($"productId={product}&type=LowStock"));
        Assert.Equal("Warning", low.GetProperty("severity").GetString());
        Assert.True(low.GetProperty("isActive").GetBoolean());

        await _client.ReceiveAsync(product, 300m, shelfLifeHours: 200);
        await factory.RunExpiryTickAsync();

        var resolved = Assert.Single(await AlertsAsync($"productId={product}&type=LowStock"));
        Assert.False(resolved.GetProperty("isActive").GetBoolean());
        Assert.Empty(await AlertsAsync($"productId={product}&type=LowStock&active=true"));
    }

    [Fact]
    public async Task Manual_write_off_partial_full_and_errors()
    {
        var product = await _client.CreateProductAsync();
        var batch = await _client.ReceiveAsync(product, 300m, shelfLifeHours: 100);
        var dish = await _client.CreateDishAsync((product, 100m));

        var partial = await WriteOffAsync(batch, new { quantity = 0.1m, unit = "Kilogram", reason = "Бой" });
        await partial.EnsureStatusAsync(HttpStatusCode.OK);
        var partialBody = await partial.ReadJsonAsync();
        Assert.Equal(100m, partialBody.GetProperty("writtenOffQty").GetDecimal());
        Assert.Equal(200m, partialBody.GetProperty("remainingQty").GetDecimal());
        Assert.Equal("Active", partialBody.GetProperty("status").GetString());

        var tooMuch = await WriteOffAsync(batch, new { quantity = 201m, reason = "Бой" });
        await tooMuch.EnsureStatusAsync(HttpStatusCode.UnprocessableEntity);
        Assert.Equal(200m, (await tooMuch.ReadJsonAsync()).GetProperty("remainingQty").GetDecimal());

        var noReason = await WriteOffAsync(batch, new { quantity = 1m, reason = " " });
        await noReason.EnsureStatusAsync(HttpStatusCode.BadRequest);
        Assert.True((await noReason.ReadJsonAsync()).GetProperty("errors").TryGetProperty("reason", out _));

        var negative = await WriteOffAsync(batch, new { quantity = -1m, reason = "Бой" });
        await negative.EnsureStatusAsync(HttpStatusCode.BadRequest);

        var full = await WriteOffAsync(batch, new { reason = "Инвентаризация" });
        await full.EnsureStatusAsync(HttpStatusCode.OK);
        Assert.Equal(200m, (await full.ReadJsonAsync()).GetProperty("writtenOffQty").GetDecimal());

        var again = await WriteOffAsync(batch, new { reason = "Инвентаризация" });
        await again.EnsureStatusAsync(HttpStatusCode.Conflict);
        Assert.Equal("batch.not_writable", (await again.ReadJsonAsync()).GetProperty("code").GetString());

        var unknown = await WriteOffAsync(Guid.NewGuid(), new { reason = "Бой" });
        await unknown.EnsureStatusAsync(HttpStatusCode.NotFound);

        // Списание пересчитало стоп-лист сразу, без тика.
        var stop = (await _client.GetFromJsonAsync<JsonElement>("/api/v1/stoplist")).EnumerateArray()
            .Single(s => s.GetProperty("dishId").GetGuid() == dish);
        Assert.NotEqual(JsonValueKind.Null, stop.GetProperty("since").ValueKind);
        Assert.Equal(2, (await MovementsAsync(batch)).Count(m => m.Type == MovementType.ManualWriteOff));
        await AssertLedgerConsistentAsync(batch);
    }

    [Fact]
    public async Task Acknowledge_is_idempotent_and_updates_summary()
    {
        var product = await _client.CreateProductAsync();
        var batch = await _client.ReceiveAsync(product, 10m, shelfLifeHours: 10);
        factory.Clock.Advance(TimeSpan.FromHours(9)); // Critical: 1 ч из 10
        await factory.RunExpiryTickAsync();
        var alert = Assert.Single(await BatchAlertsAsync(batch));
        var id = alert.GetProperty("id").GetGuid();
        var before = await _client.GetFromJsonAsync<JsonElement>("/api/v1/alerts/summary");

        var first = await (await _client.PostAsync($"/api/v1/alerts/{id}/ack", null)).ReadJsonAsync();
        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var second = await (await _client.PostAsync($"/api/v1/alerts/{id}/ack", null)).ReadJsonAsync();

        Assert.Equal(first.GetProperty("acknowledgedAt").GetDateTimeOffset(), second.GetProperty("acknowledgedAt").GetDateTimeOffset());
        var after = await _client.GetFromJsonAsync<JsonElement>("/api/v1/alerts/summary");
        Assert.Equal(before.GetProperty("critical").GetInt32() - 1, after.GetProperty("critical").GetInt32());
        Assert.Empty(await AlertsAsync($"batchId={batch}&acknowledged=false"));
        Assert.Single(await AlertsAsync($"batchId={batch}&acknowledged=true"));

        var missing = await _client.PostAsync($"/api/v1/alerts/{Guid.NewGuid()}/ack", null);
        await missing.EnsureStatusAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Parallel_ticks_write_off_exactly_once()
    {
        var product = await _client.CreateProductAsync();
        var batch = await _client.ReceiveAsync(product, 70m, shelfLifeHours: 1);
        factory.Clock.Advance(TimeSpan.FromHours(1));

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => factory.RunExpiryTickAsync()));

        Assert.Contains(results, r => !r.Skipped);
        Assert.Single(await MovementsAsync(batch), m => m.Type == MovementType.ExpiryWriteOff);
        Assert.Single(await BatchAlertsAsync(batch), a => a.GetProperty("type").GetString() == "Expired");
        await AssertLedgerConsistentAsync(batch);
    }

    private Task<HttpResponseMessage> WriteOffAsync(Guid batchId, object body) =>
        _client.PostAsJsonAsync($"/api/v1/batches/{batchId}/write-off", body);

    private Task<List<JsonElement>> BatchAlertsAsync(Guid batchId) => AlertsAsync($"batchId={batchId}");

    private async Task<List<JsonElement>> AlertsAsync(string filter) =>
        (await _client.GetFromJsonAsync<JsonElement>($"/api/v1/alerts?{filter}&pageSize=100"))
            .GetProperty("items").EnumerateArray().ToList();

    private async Task<Batch> LoadBatchAsync(Guid batchId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        using var demo = factory.AsDemoRestaurant();
        var db = scope.ServiceProvider.GetRequiredService<MiseDbContext>();
        return await db.Batches.AsNoTracking().SingleAsync(b => b.Id == batchId);
    }

    private async Task<List<StockMovement>> MovementsAsync(Guid batchId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        using var demo = factory.AsDemoRestaurant();
        var db = scope.ServiceProvider.GetRequiredService<MiseDbContext>();
        return await db.StockMovements.AsNoTracking().Where(m => m.BatchId == batchId).ToListAsync();
    }

    private async Task AssertLedgerConsistentAsync(Guid batchId)
    {
        var batch = await LoadBatchAsync(batchId);
        Assert.True(batch.RemainingQty >= 0);
        Assert.Equal(batch.RemainingQty, (await MovementsAsync(batchId)).Sum(m => m.QtyDelta));
    }
}
