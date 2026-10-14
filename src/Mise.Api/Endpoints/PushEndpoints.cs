using Mise.Api.Auth;
using Mise.Application.Push;

namespace Mise.Api.Endpoints;

internal static class PushEndpoints
{
    /// <summary>Пуши на телефон: подписка устройства текущего пользователя (любой роли сотрудника).</summary>
    public static RouteGroupBuilder MapPushEndpoints(this RouteGroupBuilder api)
    {
        var push = api.MapGroup("/push").WithTags("Пуши");
        push.MapGet("/config", (PushService service) => service.GetConfig())
            .WithSummary("Включены ли пуши на сервере и открытый ключ VAPID для подписки браузера");
        push.MapGet("/status", (string endpoint, PushService service, CancellationToken ct) => service.GetStatusAsync(endpoint, ct))
            .WithSummary("Подписано ли это устройство на пуши текущего пользователя");
        push.MapPost("/subscribe", (SubscribePushRequest request, PushService service, CancellationToken ct) => service.SubscribeAsync(request, ct))
            .WithSummary("Подписать устройство (PushSubscription.toJSON() + язык)")
            .WithDescription("Адрес подписки принимается только у известных сервисов пушей (FCM, Mozilla, Apple, Microsoft) — защита от SSRF.")
            .Audited("push.subscribe", "PushSubscription");
        push.MapPost("/unsubscribe", (UnsubscribePushRequest request, PushService service, CancellationToken ct) => service.UnsubscribeAsync(request, ct))
            .WithSummary("Отписать устройство")
            .Audited("push.unsubscribe", "PushSubscription");
        push.MapPost("/test", async (PushService service, CancellationToken ct) => new { sent = await service.SendTestAsync(ct) })
            .WithSummary("Проверочный пуш на устройства текущего пользователя")
            .RequireRateLimiting(RateLimitingOptions.LoginPolicy);
        return api;
    }
}
