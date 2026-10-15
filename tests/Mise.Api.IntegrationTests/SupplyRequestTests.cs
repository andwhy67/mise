using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mise.Api.IntegrationTests;

/// <summary>Заявка поставщику: номер, ориентировочная сумма, запись в журнале действий.</summary>
[Collection(ApiCollection.Name)]
public sealed class SupplyRequestTests(MiseApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Request_gets_a_number_estimated_cost_and_an_audit_entry()
    {
        var priced = await _client.CreateProductAsync();
        var card = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/products/{priced}");
        await (await _client.PutAsJsonAsync($"/api/v1/products/{priced}", new
        {
            name = card.GetProperty("name").GetString(), unit = "Gram", defaultShelfLifeHours = 72, minStockQty = 0m, packSize = 1m,
            referencePrice = 900m,
        })).EnsureStatusAsync(HttpStatusCode.OK);
        var unpriced = await _client.CreateProductAsync(unit: "Piece");

        var response = await _client.PostAsJsonAsync("/api/v1/supply-requests", new
        {
            supplier = "  Рыбный двор ",
            lines = new object[] { new { productId = priced, quantity = 2500m }, new { productId = unpriced, quantity = 30m } },
        });

        await response.EnsureStatusAsync(HttpStatusCode.OK);
        var request = await response.ReadJsonAsync();
        var number = request.GetProperty("number").GetString()!;
        Assert.Matches(@"^REQ-\d{8}-\d{4}-[0-9A-F]{4}$", number);
        Assert.Equal("Рыбный двор", request.GetProperty("supplier").GetString());
        Assert.Equal(2250m, request.GetProperty("estimatedTotal").GetDecimal()); // 2,5 кг × 900 ₽
        Assert.Equal(1, request.GetProperty("unpricedLines").GetInt32());
        Assert.Equal(900m, request.GetProperty("lines")[0].GetProperty("price").GetDecimal());

        var audit = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/audit?action=supply_request.create&entityId={number}");
        var entry = Assert.Single(audit.GetProperty("items").EnumerateArray());
        Assert.Equal("Рыбный двор", entry.GetProperty("details").GetProperty("supplier").GetString());
        Assert.Equal(2, entry.GetProperty("details").GetProperty("lines").GetArrayLength());
    }

    [Fact]
    public async Task Empty_or_unknown_lines_are_rejected()
    {
        await (await _client.PostAsJsonAsync("/api/v1/supply-requests", new { supplier = "X", lines = Array.Empty<object>() }))
            .EnsureStatusAsync(HttpStatusCode.BadRequest);
        await (await _client.PostAsJsonAsync("/api/v1/supply-requests", new { lines = new[] { new { productId = Guid.NewGuid(), quantity = 1m } } }))
            .EnsureStatusAsync(HttpStatusCode.BadRequest);
    }
}
