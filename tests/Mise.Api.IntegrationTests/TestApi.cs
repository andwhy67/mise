using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mise.Api.IntegrationTests;

/// <summary>Короткие хелперы для сценариев: уникальные продукты, поставки, чтение остатков.</summary>
internal static class TestApi
{
    public static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 13, 64)];

    public static async Task<Guid> CreateProductAsync(this HttpClient client, string unit = "Gram", int? defaultShelfLifeHours = 72,
        decimal minStockQty = 0m)
    {
        var response = await client.PostAsJsonAsync("/api/v1/products", new
        {
            name = Unique("Продукт"),
            unit,
            defaultShelfLifeHours,
            minStockQty,
            packSize = 1m,
            category = "Тест",
        });
        await response.EnsureStatusAsync(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public static async Task<HttpResponseMessage> PostSupplyAsync(this HttpClient client, object body, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/supplies") { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    public static async Task<JsonElement> CreateSupplyAsync(this HttpClient client, object body, string? idempotencyKey = null)
    {
        var response = await client.PostSupplyAsync(body, idempotencyKey);
        await response.EnsureStatusAsync(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task<JsonElement> GetStockItemAsync(this HttpClient client, Guid productId)
    {
        var stock = await client.GetFromJsonAsync<JsonElement>("/api/v1/stock");
        return stock.EnumerateArray().Single(s => s.GetProperty("productId").GetGuid() == productId);
    }

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    public static async Task EnsureStatusAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
            throw new Xunit.Sdk.XunitException(
                $"Ожидался {(int)expected}, получен {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
}
