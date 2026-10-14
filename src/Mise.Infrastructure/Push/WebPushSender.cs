using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mise.Application.Push;
using Mise.Domain.Abstractions;
using Mise.Domain.Alerts;

namespace Mise.Infrastructure.Push;

/// <summary>
/// Отправка пуша по протоколу Web Push (RFC 8030): тело шифруется ключами устройства (RFC 8291),
/// запрос подписывается VAPID (RFC 8292). Адрес — только из разрешённого списка (защита от SSRF).
/// </summary>
public sealed class WebPushSender(HttpClient http, IOptions<PushOptions> options, IClock clock, ILogger<WebPushSender> logger) : IPushSender
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Lazy<string?> publicKey = new(() => options.Value.Enabled ? WebPushCrypto.PublicKeyFor(options.Value.PrivateKey!) : null);

    public bool IsConfigured => options.Value.Enabled;
    public string? PublicKey => publicKey.Value;

    public async Task<PushSendResult> SendAsync(PushEndpoint target, PushMessage message, CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.Enabled || !settings.IsAllowedEndpoint(target.Endpoint))
            return PushSendResult.Gone;

        try
        {
            // Тело — то, что читает service worker (src/sw/payload.ts).
            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                title = message.Title,
                body = message.Body,
                url = message.Url,
                tag = message.Tag,
                severity = message.Severity.ToString(),
                lang = message.Language,
            }, Json);
            var body = WebPushCrypto.Encrypt(payload, Base64Url.Decode(target.P256dh), Base64Url.Decode(target.Auth));
            var token = WebPushCrypto.CreateVapidToken(target.Endpoint, settings.Subject, settings.PrivateKey!, clock.UtcNow);

            using var request = new HttpRequestMessage(HttpMethod.Post, target.Endpoint) { Content = new ByteArrayContent(body) };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            request.Content.Headers.ContentEncoding.Add("aes128gcm");
            request.Headers.TryAddWithoutValidation("Authorization", $"vapid t={token}, k={PublicKey}");
            // Пока телефон офлайн, сервис хранит пуш сутки; критичное доставляется в приоритете.
            request.Headers.Add("TTL", "86400");
            request.Headers.Add("Urgency", message.Severity == AlertSeverity.Critical ? "high" : "normal");

            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
                return PushSendResult.Sent;
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                return PushSendResult.Gone;

            logger.LogWarning("Push service {Host} answered {Status}", new Uri(target.Endpoint).Host, (int)response.StatusCode);
            return PushSendResult.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException or ArgumentException)
        {
            // Битые ключи подписки (FormatException/ArgumentException) — это тоже «временная» неудача: после серии подписка удалится.
            logger.LogWarning(ex, "Push to {Host} failed", SafeHost(target.Endpoint));
            return PushSendResult.Failed;
        }
    }

    private static string SafeHost(string endpoint) => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri.Host : "?";
}

/// <summary>Генерация пары ключей VAPID для .env: <c>dotnet run --project src/Mise.Api -- --generate-vapid-keys</c>.</summary>
public static class VapidKeyGenerator
{
    public static string Describe()
    {
        var keys = WebPushCrypto.GenerateVapidKeys();
        return new StringBuilder()
            .AppendLine("# Web Push (VAPID) — добавьте в .env; открытый ключ сервер выводит сам из закрытого.")
            .AppendLine($"PUSH_PRIVATE_KEY={keys.PrivateKey}")
            .AppendLine($"# открытый ключ (для справки): {keys.PublicKey}")
            .ToString();
    }
}
