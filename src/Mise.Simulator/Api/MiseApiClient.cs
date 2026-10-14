using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mise.Application.Simulator;
using Mise.Simulator.Model;

namespace Mise.Simulator.Api;

public enum OutcomeKind
{
    Created,
    /// <summary>Идемпотентный повтор: API вернул прежний результат (200).</summary>
    Replayed,
    /// <summary>Бизнес-отказ (422) — для заказа это нормальный исход «не хватает ингредиентов».</summary>
    Rejected,
    Conflict,
    Failed,
}

/// <summary>Результат вызова API. Для действий без создания (отмена, смена статуса) успех — 200, то есть <see cref="OutcomeKind.Replayed"/>.</summary>
public sealed record ApiOutcome(OutcomeKind Kind, Guid? Id = null, string? Number = null, string? Detail = null)
{
    public bool IsSuccess => Kind is OutcomeKind.Created or OutcomeKind.Replayed;
}

/// <summary>Симулятор — внешний клиент API (PLAN §2): ходит по REST, как любая касса или склад.</summary>
public interface IMiseApi
{
    Task<IReadOnlyList<ProductInfo>> GetProductsAsync(CancellationToken ct);
    Task<IReadOnlyList<DishInfo>> GetDishesAsync(CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, decimal>> GetUsableStockAsync(CancellationToken ct);
    Task<ApiOutcome> CreateOrderAsync(IReadOnlyList<OrderLine> lines, string idempotencyKey, CancellationToken ct);
    Task<ApiOutcome> CreateSupplyAsync(SupplyDraft draft, string idempotencyKey, CancellationToken ct);
    Task<ApiOutcome> CancelOrderAsync(Guid orderId, CancellationToken ct);
    Task<ApiOutcome> CompleteOrderAsync(Guid orderId, CancellationToken ct);
}

public sealed class MiseApiClient(HttpClient http) : IMiseApi
{
    private const string IdempotencyHeader = "Idempotency-Key";
    private static readonly JsonSerializerOptions Json = SimulatorProtocol.Json;

    public async Task<IReadOnlyList<ProductInfo>> GetProductsAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<List<ProductInfo>>("/api/v1/products", Json, ct) ?? [];

    public async Task<IReadOnlyList<DishInfo>> GetDishesAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<List<DishInfo>>("/api/v1/dishes", Json, ct) ?? [];

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetUsableStockAsync(CancellationToken ct) =>
        (await http.GetFromJsonAsync<List<StockInfo>>("/api/v1/stock", Json, ct) ?? []).ToDictionary(s => s.ProductId, s => s.UsableQty);

    public async Task<ApiOutcome> CreateOrderAsync(IReadOnlyList<OrderLine> lines, string idempotencyKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(new { items = lines.Select(l => new { dishId = l.DishId, quantity = l.Quantity }) }, options: Json),
        };
        request.Headers.Add(IdempotencyHeader, idempotencyKey);
        using var response = await http.SendAsync(request, ct);
        return await ToOutcomeAsync(response, ct);
    }

    public async Task<ApiOutcome> CreateSupplyAsync(SupplyDraft draft, string idempotencyKey, CancellationToken ct)
    {
        var body = new
        {
            number = draft.Number,
            supplier = draft.Supplier,
            items = draft.Items.Select(i => new { productId = i.ProductId, quantity = i.Quantity, shelfLifeHours = i.ShelfLifeHours, expiresAt = i.ExpiresAt }),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/supplies") { Content = JsonContent.Create(body, options: Json) };
        request.Headers.Add(IdempotencyHeader, idempotencyKey);
        using var response = await http.SendAsync(request, ct);
        return await ToOutcomeAsync(response, ct);
    }

    public async Task<ApiOutcome> CancelOrderAsync(Guid orderId, CancellationToken ct)
    {
        using var response = await http.PostAsync($"/api/v1/orders/{orderId}/cancel", content: null, ct);
        return await ToOutcomeAsync(response, ct);
    }

    /// <summary>Заказ приготовлен и выдан: два шага, как на раздаче (Готов → Выдан).</summary>
    public async Task<ApiOutcome> CompleteOrderAsync(Guid orderId, CancellationToken ct)
    {
        using (var done = await http.PatchAsJsonAsync($"/api/v1/orders/{orderId}/status", new { status = "Done" }, Json, ct))
        {
            var outcome = await ToOutcomeAsync(done, ct);
            if (outcome.Kind is not (OutcomeKind.Created or OutcomeKind.Replayed))
                return outcome;
        }
        using var served = await http.PatchAsJsonAsync($"/api/v1/orders/{orderId}/status", new { status = "Served" }, Json, ct);
        return await ToOutcomeAsync(served, ct);
    }

    private static async Task<ApiOutcome> ToOutcomeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await ReadJsonAsync(response, ct);
        return response.StatusCode switch
        {
            HttpStatusCode.Created => new ApiOutcome(OutcomeKind.Created, Id(body), Number(body)),
            HttpStatusCode.OK => new ApiOutcome(OutcomeKind.Replayed, Id(body), Number(body)),
            HttpStatusCode.UnprocessableEntity => new ApiOutcome(OutcomeKind.Rejected, Detail: Missing(body) ?? Detail(body)),
            HttpStatusCode.Conflict => new ApiOutcome(OutcomeKind.Conflict, Detail: Detail(body)),
            _ => new ApiOutcome(OutcomeKind.Failed, Detail: $"HTTP {(int)response.StatusCode}: {Detail(body)}"),
        };
    }

    private static async Task<JsonElement?> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength == 0)
            return null;
        try
        {
            return await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Guid? Id(JsonElement? body) =>
        body is { ValueKind: JsonValueKind.Object } b && b.TryGetProperty("id", out var id) && id.TryGetGuid(out var guid) ? guid : null;

    private static string? Number(JsonElement? body) =>
        body is { ValueKind: JsonValueKind.Object } b && b.TryGetProperty("number", out var n) ? n.GetString() : null;

    private static string? Detail(JsonElement? body) =>
        body is { ValueKind: JsonValueKind.Object } b && b.TryGetProperty("detail", out var d) ? d.GetString() : null;

    /// <summary>Из тела 422 заказа: «Бекон, Сливки 33%» — чего не хватило.</summary>
    private static string? Missing(JsonElement? body) =>
        body is { ValueKind: JsonValueKind.Object } b && b.TryGetProperty("missing", out var missing) && missing.ValueKind == JsonValueKind.Array
            ? string.Join(", ", missing.EnumerateArray().Select(m => m.GetProperty("productName").GetString()))
            : null;
}
