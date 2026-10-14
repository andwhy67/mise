using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Mise.Domain.Users;
using Mise.Infrastructure.Push;

namespace Mise.Api.IntegrationTests;

/// <summary>Пуши на телефон: подписка устройства, защита от SSRF, рассылка по ролям, уборка мёртвых подписок.</summary>
[Collection(ApiCollection.Name)]
public sealed class PushTests(MiseApiFactory factory)
{
    private readonly HttpClient _manager = factory.CreateClient();

    [Fact]
    public async Task Config_gives_the_browser_the_public_vapid_key()
    {
        var config = await _manager.GetFromJsonAsync<JsonElement>("/api/v1/push/config");

        Assert.True(config.GetProperty("enabled").GetBoolean());
        Assert.Equal(factory.Push.Keys.PublicKey, config.GetProperty("publicKey").GetString());
    }

    [Theory]
    [InlineData("http://postgres:5432/push")]
    [InlineData("https://evil.example.com/push")]
    [InlineData("https://127.0.0.1/push")]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc")]
    public async Task Only_known_push_services_are_accepted(string endpoint)
    {
        var response = await _manager.PostAsJsonAsync("/api/v1/push/subscribe", Subscription(endpoint));

        await response.EnsureStatusAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Kitchen_gets_a_push_when_a_dish_goes_into_the_stop_list_and_warehouse_does_not()
    {
        var chef = factory.CreateClientAs(UserRole.Chef);
        var warehouse = factory.CreateClientAs(UserRole.Warehouse);
        var chefDevice = Endpoint();
        var warehouseDevice = Endpoint();
        await (await chef.PostAsJsonAsync("/api/v1/push/subscribe", Subscription(chefDevice))).EnsureStatusAsync(HttpStatusCode.OK);
        await (await warehouse.PostAsJsonAsync("/api/v1/push/subscribe", Subscription(warehouseDevice))).EnsureStatusAsync(HttpStatusCode.OK);

        var product = await _manager.CreateProductAsync();
        await _manager.ReceiveAsync(product, 1000);
        var dish = await _manager.CreateDishAsync((product, 100m));
        var card = await _manager.GetFromJsonAsync<JsonElement>($"/api/v1/dishes/{dish}");
        var name = card.GetProperty("name").GetString()!;
        await (await _manager.PutAsJsonAsync($"/api/v1/dishes/{dish}", new { name, category = (string?)null, price = 100m, isActive = false }))
            .EnsureStatusAsync(HttpStatusCode.OK);

        var push = await WaitForAsync(chefDevice, m => m.Title == $"Нельзя заказать: {name}");
        Assert.Equal("Убрано из меню вручную.", push.Body);
        Assert.Equal("/stoplist", push.Url);
        Assert.DoesNotContain(factory.Push.Sent, s => s.Endpoint == warehouseDevice && s.Message.Title.Contains(name, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Device_language_decides_the_push_text()
    {
        var device = Endpoint();
        await (await _manager.PostAsJsonAsync("/api/v1/push/subscribe", Subscription(device, "en-US"))).EnsureStatusAsync(HttpStatusCode.OK);

        await (await _manager.PostAsync("/api/v1/push/test", null)).EnsureStatusAsync(HttpStatusCode.OK);

        var push = await WaitForAsync(device, m => m.Tag == "mise:test");
        Assert.Equal("Notifications are on", push.Title);
    }

    [Fact]
    public async Task Gone_subscription_is_removed_and_unsubscribe_works()
    {
        var gone = $"https://fcm.googleapis.com/fcm/send/gone-{Guid.NewGuid():N}";
        var kept = Endpoint();
        await (await _manager.PostAsJsonAsync("/api/v1/push/subscribe", Subscription(gone))).EnsureStatusAsync(HttpStatusCode.OK);
        await (await _manager.PostAsJsonAsync("/api/v1/push/subscribe", Subscription(kept))).EnsureStatusAsync(HttpStatusCode.OK);

        await (await _manager.PostAsync("/api/v1/push/test", null)).EnsureStatusAsync(HttpStatusCode.OK);
        Assert.False(await SubscribedAsync(gone));
        Assert.True(await SubscribedAsync(kept));

        await (await _manager.PostAsJsonAsync("/api/v1/push/unsubscribe", new { endpoint = kept })).EnsureStatusAsync(HttpStatusCode.OK);
        Assert.False(await SubscribedAsync(kept));
    }

    private async Task<bool> SubscribedAsync(string endpoint) =>
        (await _manager.GetFromJsonAsync<JsonElement>($"/api/v1/push/status?endpoint={Uri.EscapeDataString(endpoint)}")).GetProperty("subscribed").GetBoolean();

    /// <summary>Рассылка идёт в фоне после окна real-time: ждём нужное сообщение до 10 секунд.</summary>
    private async Task<Application.Push.PushMessage> WaitForAsync(string endpoint, Func<Application.Push.PushMessage, bool> match)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var found = factory.Push.Sent.FirstOrDefault(s => s.Endpoint == endpoint && match(s.Message));
            if (found.Message is not null)
                return found.Message;
            await Task.Delay(100);
        }
        throw new Xunit.Sdk.XunitException($"Пуш на {endpoint} не пришёл. Отправлено: {string.Join("; ", factory.Push.Sent.Select(s => s.Message.Title))}");
    }

    private static string Endpoint() => $"https://fcm.googleapis.com/fcm/send/{Guid.NewGuid():N}";

    /// <summary>Подписка как из браузера: настоящий открытый ключ P-256 и 16-байтовый auth.</summary>
    private static object Subscription(string endpoint, string language = "ru")
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return new
        {
            endpoint,
            keys = new { p256dh = Base64Url.Encode(WebPushCrypto.ExportPublicKey(key)), auth = Base64Url.Encode(RandomNumberGenerator.GetBytes(16)) },
            language,
        };
    }
}
