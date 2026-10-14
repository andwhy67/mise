using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Mise.Domain.Users;

namespace Mise.Api.IntegrationTests;

/// <summary>Клиент хаба /hubs/stock поверх TestServer: записывает все события и ждёт нужное.</summary>
internal sealed class HubRecorder : IAsyncDisposable
{
    public static readonly string[] EventNames =
    [
        "StockChanged", "AvailabilityChanged", "StopListChanged", "AlertRaised", "BatchStatusChanged",
        "OrderCreated", "OrderStatusChanged", "SupplyReceived", "SimulatorStateChanged", "HotOffersChanged",
    ];

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly HubConnection _connection;
    private readonly ConcurrentQueue<(string Name, JsonElement Payload)> _events = new();

    private HubRecorder(HubConnection connection)
    {
        _connection = connection;
        foreach (var name in EventNames)
            connection.On<JsonElement>(name, payload => _events.Enqueue((name, payload)));
    }

    public static async Task<HubRecorder> ConnectAsync(MiseApiFactory factory, UserRole role = UserRole.Manager)
    {
        var token = factory.TokenFor(role);
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/stock"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        var recorder = new HubRecorder(connection);
        await connection.StartAsync();
        return recorder;
    }

    public IReadOnlyList<(string Name, JsonElement Payload)> Events => _events.ToList();

    /// <summary>Ждёт событие с указанным именем, для которого выполняется условие (по всем уже полученным).</summary>
    public async Task<JsonElement> WaitForAsync(string name, Func<JsonElement, bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var (eventName, payload) in _events)
            {
                if (eventName == name && predicate(payload))
                    return payload;
            }
            await Task.Delay(25);
        }

        var received = string.Join(", ", _events.Select(e => e.Name));
        throw new Xunit.Sdk.XunitException($"Событие {name} с нужным содержимым не пришло. Получены: [{received}]");
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}

internal static class JsonElementExtensions
{
    public static IEnumerable<JsonElement> Items(this JsonElement payload, string property) =>
        payload.GetProperty(property).EnumerateArray();

    public static bool Has(this JsonElement element, string property, Guid id) =>
        element.GetProperty(property).ValueKind == JsonValueKind.String && element.GetProperty(property).GetGuid() == id;
}
