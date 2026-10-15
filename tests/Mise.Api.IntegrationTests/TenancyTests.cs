using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mise.Api.IntegrationTests;

/// <summary>
/// Рестораны — отдельные организации: регистрация, приглашения и главное — ничего из одного ресторана
/// не видно и не меняется из другого (демо-ресторан тестов — «сосед»).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TenancyTests(MiseApiFactory factory)
{
    private const string Password = "Strong-Pass-123";
    private readonly HttpClient _demo = factory.CreateClient();

    private async Task<(HttpClient Client, JsonElement Session)> RegisterAsync(bool fillCatalog = false, string name = "Бистро")
    {
        var anonymous = factory.CreateClientAs(null);
        var response = await anonymous.PostAsJsonAsync("/api/v1/auth/register", new
        {
            restaurantName = name,
            displayName = "Анна",
            login = TestApi.Unique("owner").ToLowerInvariant(),
            password = Password,
            fillDemoCatalog = fillCatalog,
        });
        await response.EnsureStatusAsync(HttpStatusCode.OK);
        var session = await response.ReadJsonAsync();
        return (WithToken(session), session);
    }

    private HttpClient WithToken(JsonElement session)
    {
        var client = factory.CreateClientAs(null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        return client;
    }

    [Fact]
    public async Task Registered_restaurant_starts_empty_and_its_creator_is_the_manager()
    {
        var (client, session) = await RegisterAsync(name: "Бистро «Шпинат»");

        Assert.Equal("Manager", session.GetProperty("user").GetProperty("role").GetString());
        Assert.Equal("Бистро «Шпинат»", session.GetProperty("restaurant").GetProperty("name").GetString());
        Assert.False(session.GetProperty("restaurant").GetProperty("isDemo").GetBoolean());
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/v1/products")).EnumerateArray());
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/v1/stock")).EnumerateArray());
        Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/v1/users")).EnumerateArray());
    }

    [Fact]
    public async Task Restaurants_do_not_see_or_touch_each_others_data()
    {
        var (other, _) = await RegisterAsync();
        // То же название, что у демо-продукта: уникальность — внутри ресторана.
        var create = await other.PostAsJsonAsync("/api/v1/products", new { name = "Шпинат", unit = "Gram", defaultShelfLifeHours = 48, minStockQty = 0m, packSize = 1m });
        await create.EnsureStatusAsync(HttpStatusCode.Created);
        var spinach = (await create.ReadJsonAsync()).GetProperty("id").GetGuid();
        await other.ReceiveAsync(spinach, 500m);
        var dish = await other.CreateDishAsync((spinach, 100m));

        // Демо-ресторан не видит чужой продукт ни списком, ни по id, ни на складе — и не может заказать чужое блюдо.
        Assert.DoesNotContain((await _demo.GetFromJsonAsync<JsonElement>("/api/v1/products")).EnumerateArray(), p => p.GetProperty("id").GetGuid() == spinach);
        Assert.Equal(HttpStatusCode.NotFound, (await _demo.GetAsync($"/api/v1/products/{spinach}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _demo.GetAsync($"/api/v1/stock/{spinach}/batches")).StatusCode);
        var order = await _demo.PostOrderAsync(null, (dish, 1));
        Assert.Equal(HttpStatusCode.BadRequest, order.StatusCode);
        Assert.Equal(500m, await other.UsableAsync(spinach));

        // Нумерация заказов — своя у каждого ресторана.
        var placed = await other.CreateOrderAsync((dish, 1));
        Assert.Equal("ORD-000001", placed.GetProperty("number").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await _demo.GetAsync($"/api/v1/orders/{placed.GetProperty("id").GetGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Demo_catalog_is_copied_with_own_ids()
    {
        var (client, _) = await RegisterAsync(fillCatalog: true);

        var products = (await client.GetFromJsonAsync<JsonElement>("/api/v1/products")).EnumerateArray().ToList();
        var dishes = (await client.GetFromJsonAsync<JsonElement>("/api/v1/dishes")).EnumerateArray().ToList();
        var demoProducts = (await _demo.GetFromJsonAsync<JsonElement>("/api/v1/products")).EnumerateArray()
            .Select(p => p.GetProperty("id").GetGuid()).ToHashSet();

        Assert.Equal(26, products.Count);
        Assert.Equal(17, dishes.Count);
        var own = products.Select(p => p.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Empty(own.Intersect(demoProducts));
        Assert.All(dishes.SelectMany(d => d.GetProperty("ingredients").EnumerateArray()), i => Assert.Contains(i.GetProperty("productId").GetGuid(), own));
        var spinach = products.Single(p => p.GetProperty("name").GetString() == "Шпинат");
        Assert.Contains(spinach.GetProperty("substitutes")[0].GetProperty("productId").GetGuid(), own);
    }

    [Fact]
    public async Task Invite_brings_an_employee_into_exactly_that_restaurant_once()
    {
        var (manager, owner) = await RegisterAsync(name: "Кафе у моста");
        var created = await manager.PostAsJsonAsync("/api/v1/invites", new { role = "Chef" });
        await created.EnsureStatusAsync(HttpStatusCode.Created);
        var code = (await created.ReadJsonAsync()).GetProperty("code").GetString()!;
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}$", code);

        var anonymous = factory.CreateClientAs(null);
        var preview = await anonymous.GetFromJsonAsync<JsonElement>($"/api/v1/auth/invites/{code.ToLowerInvariant().Replace("-", " ")}");
        Assert.Equal("Кафе у моста", preview.GetProperty("restaurantName").GetString());
        Assert.Equal("Chef", preview.GetProperty("role").GetString());

        var join = await anonymous.PostAsJsonAsync("/api/v1/auth/join", new { code, displayName = "Пётр", login = TestApi.Unique("cook").ToLowerInvariant(), password = Password });
        await join.EnsureStatusAsync(HttpStatusCode.OK);
        var chefSession = await join.ReadJsonAsync();
        Assert.Equal("Chef", chefSession.GetProperty("user").GetProperty("role").GetString());
        Assert.Equal(owner.GetProperty("restaurant").GetProperty("id").GetGuid(), chefSession.GetProperty("restaurant").GetProperty("id").GetGuid());

        // Код одноразовый; повар приглашать не может; менеджер видит, кто пришёл.
        var again = await anonymous.PostAsJsonAsync("/api/v1/auth/join", new { code, displayName = "Иван", login = TestApi.Unique("cook").ToLowerInvariant(), password = Password });
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await WithToken(chefSession).PostAsJsonAsync("/api/v1/invites", new { role = "Chef" })).StatusCode);
        var invites = (await manager.GetFromJsonAsync<JsonElement>("/api/v1/invites")).EnumerateArray().ToList();
        var used = Assert.Single(invites);
        Assert.Equal("Accepted", used.GetProperty("status").GetString());
        Assert.Equal("Пётр", used.GetProperty("acceptedBy").GetString());
        Assert.Equal(2, (await manager.GetFromJsonAsync<JsonElement>("/api/v1/users")).GetArrayLength());
    }

    [Fact]
    public async Task Revoked_invite_and_taken_login_are_rejected()
    {
        var (manager, owner) = await RegisterAsync();
        var invite = await (await manager.PostAsJsonAsync("/api/v1/invites", new { role = "Warehouse" })).ReadJsonAsync();
        await (await manager.DeleteAsync($"/api/v1/invites/{invite.GetProperty("id").GetGuid()}")).EnsureStatusAsync(HttpStatusCode.OK);

        var anonymous = factory.CreateClientAs(null);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/auth/invites/{invite.GetProperty("code").GetString()}")).StatusCode);

        // Логин уникален во всей системе: демо-логин «chef» занят и для нового ресторана.
        var taken = await anonymous.PostAsJsonAsync("/api/v1/auth/register", new { restaurantName = "Ещё одно", displayName = "Б", login = "chef", password = Password });
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsJsonAsync("/api/v1/users",
            new { login = "warehouse", displayName = "Склад", role = "Warehouse", password = Password })).StatusCode);
        Assert.False(string.IsNullOrEmpty(owner.GetProperty("accessToken").GetString()));
    }

    [Fact]
    public async Task Simulator_and_demo_stock_are_only_for_the_demo_restaurant()
    {
        var (client, _) = await RegisterAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/simulator/state")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/v1/demo/seed-stock", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _demo.GetAsync("/api/v1/simulator/state")).StatusCode);
    }

    [Fact]
    public async Task Realtime_events_stay_inside_the_restaurant()
    {
        await using var demoHub = await HubRecorder.ConnectAsync(factory);
        var (other, _) = await RegisterAsync();
        var product = await other.CreateProductAsync();

        await other.ReceiveAsync(product, 300m);
        // Сигнал в демо-ресторане приходит — значит, хаб жив; чужой поставки среди событий нет.
        var demoProduct = await _demo.CreateProductAsync();
        await _demo.ReceiveAsync(demoProduct, 100m);
        await demoHub.WaitForAsync("StockChanged", e => e.GetProperty("items").EnumerateArray().Any(i => i.GetProperty("productId").GetGuid() == demoProduct));

        Assert.DoesNotContain(demoHub.Events, e => e.Payload.ToString().Contains(product.ToString()));
    }
}
