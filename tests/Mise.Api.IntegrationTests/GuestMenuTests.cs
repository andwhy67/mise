using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mise.Api.IntegrationTests;

/// <summary>
/// QR-меню: гость без входа видит меню по ключу и заказывает; заказ проходит обычные проверки и попадает на кухню
/// со столом и способом оплаты; склад наружу не виден; выключенный приём и старый ключ — отказ.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class GuestMenuTests(MiseApiFactory factory)
{
    private readonly HttpClient _manager = factory.CreateClient();

    private async Task<string> MenuKeyAsync(HttpClient manager) =>
        (await manager.GetFromJsonAsync<JsonElement>("/api/v1/guest-menu")).GetProperty("menuKey").GetString()!;

    private static Task<HttpResponseMessage> OrderAsync(HttpClient guest, string key, Guid dish, int quantity, int? table = 3, string payment = "Sbp") =>
        guest.PostAsJsonAsync($"/api/v1/public/menu/{key}/orders", new { items = new[] { new { dishId = dish, quantity } }, table, payment });

    [Fact]
    public async Task Guest_orders_without_login_and_the_kitchen_sees_table_and_payment()
    {
        var product = await _manager.CreateProductAsync();
        await _manager.ReceiveAsync(product, 1000m);
        var dish = await _manager.CreateDishAsync((product, 100m));
        var key = await MenuKeyAsync(_manager);
        var guest = factory.CreateClientAs(null);

        var menu = await guest.GetFromJsonAsync<JsonElement>($"/api/v1/public/menu/{key}");
        var item = menu.GetProperty("dishes").EnumerateArray().Single(d => d.GetProperty("id").GetGuid() == dish);
        Assert.Equal("Available", item.GetProperty("state").GetString());
        // Склад наружу не отдаётся: ни остатков, ни порций.
        Assert.DoesNotContain("usable", menu.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("maxPortions\":", item.GetRawText(), StringComparison.Ordinal);

        var response = await OrderAsync(guest, key, dish, 2);
        await response.EnsureStatusAsync(HttpStatusCode.OK);
        var order = await response.ReadJsonAsync();
        var token = order.GetProperty("token").GetString()!;
        Assert.Equal("Created", order.GetProperty("status").GetString());
        Assert.Equal(3, order.GetProperty("table").GetInt32());

        Assert.Equal(800m, await _manager.UsableAsync(product));
        var card = (await _manager.GetFromJsonAsync<JsonElement>("/api/v1/kitchen/orders")).EnumerateArray()
            .Single(o => o.GetProperty("number").GetString() == order.GetProperty("number").GetString());
        Assert.Equal("Guest", card.GetProperty("source").GetString());
        Assert.Equal(3, card.GetProperty("tableNumber").GetInt32());
        Assert.Equal("Sbp", card.GetProperty("payment").GetString());

        var status = await guest.GetFromJsonAsync<JsonElement>($"/api/v1/public/menu/{key}/orders/{token}");
        Assert.Equal(order.GetProperty("number").GetString(), status.GetProperty("number").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/v1/public/menu/{key}/orders/{token[..^1]}x")).StatusCode);
    }

    [Fact]
    public async Task Shortage_names_dishes_but_not_grams_and_too_many_orders_are_throttled()
    {
        var product = await _manager.CreateProductAsync();
        await _manager.ReceiveAsync(product, 150m);
        var dish = await _manager.CreateDishAsync((product, 100m));
        var key = await MenuKeyAsync(_manager);
        var guest = factory.CreateClientAs(null);

        var refused = await OrderAsync(guest, key, dish, 2);
        Assert.Equal((HttpStatusCode)422, refused.StatusCode);
        var problem = await refused.ReadJsonAsync();
        Assert.Equal("guest.unavailable", problem.GetProperty("code").GetString());
        Assert.False(problem.TryGetProperty("missing", out _));
        Assert.Equal(150m, await _manager.UsableAsync(product));

        Assert.Equal(HttpStatusCode.BadRequest, (await OrderAsync(guest, key, dish, 21)).StatusCode);
        // 3 заказа за 10 минут с одного адреса (отказы тоже считаются) — дальше 429.
        Assert.Equal(HttpStatusCode.OK, (await OrderAsync(guest, key, dish, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await OrderAsync(guest, key, dish, 1)).StatusCode);
    }

    [Fact]
    public async Task Ordering_can_be_switched_off_and_a_new_key_retires_old_qr_codes()
    {
        var owner = factory.CreateClientAs(null);
        var registered = await owner.PostAsJsonAsync("/api/v1/auth/register", new
        {
            restaurantName = "Кафе у дома",
            displayName = "Анна",
            login = TestApi.Unique("qr").ToLowerInvariant(),
            password = "Strong-Pass-123",
            fillDemoCatalog = true,
        });
        await registered.EnsureStatusAsync(HttpStatusCode.OK);
        var manager = factory.CreateClientAs(null);
        manager.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await registered.ReadJsonAsync()).GetProperty("accessToken").GetString());
        var key = await MenuKeyAsync(manager);
        var guest = factory.CreateClientAs(null);

        var menu = await guest.GetFromJsonAsync<JsonElement>($"/api/v1/public/menu/{key}");
        Assert.Equal("Кафе у дома", menu.GetProperty("restaurant").GetString());
        // Меню своего ресторана: блюда, созданные в демо-ресторане тестов, сюда не попадают.
        var demoDishes = (await _manager.GetFromJsonAsync<JsonElement>("/api/v1/dishes")).EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).ToHashSet();
        Assert.DoesNotContain(menu.GetProperty("dishes").EnumerateArray(), d => demoDishes.Contains(d.GetProperty("id").GetGuid()));

        await (await manager.PutAsJsonAsync("/api/v1/guest-menu", new { ordering = false, tables = 5 })).EnsureStatusAsync(HttpStatusCode.OK);
        var dish = menu.GetProperty("dishes")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await OrderAsync(guest, key, dish, 1, table: 1)).StatusCode);
        Assert.False((await guest.GetFromJsonAsync<JsonElement>($"/api/v1/public/menu/{key}")).GetProperty("ordering").GetBoolean());

        var renewed = await (await manager.PostAsync("/api/v1/guest-menu/regenerate", null)).ReadJsonAsync();
        Assert.NotEqual(key, renewed.GetProperty("menuKey").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/v1/public/menu/{key}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientAs(Domain.Users.UserRole.Chef).GetAsync("/api/v1/guest-menu")).StatusCode);
    }
}
