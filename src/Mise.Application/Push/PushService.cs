using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Mise.Application.Abstractions;
using Mise.Application.Auth;
using Mise.Application.Common;
using Mise.Domain.Abstractions;
using Mise.Domain.Alerts;
using Mise.Domain.Push;

namespace Mise.Application.Push;

/// <summary>Подписка текущего пользователя на пуши этого устройства.</summary>
public sealed class PushService(
    IMiseDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    IPushSender sender,
    IValidator<SubscribePushRequest> subscribeValidator,
    IValidator<UnsubscribePushRequest> unsubscribeValidator)
{
    public PushConfigDto GetConfig() => new(sender.IsConfigured, sender.PublicKey);

    public async Task<PushStatusDto> GetStatusAsync(string endpoint, CancellationToken ct)
    {
        var userId = RequireUser();
        return new PushStatusDto(await db.PushSubscriptions.AnyAsync(s => s.Endpoint == endpoint && s.UserId == userId, ct));
    }

    public async Task<PushStatusDto> SubscribeAsync(SubscribePushRequest request, CancellationToken ct)
    {
        await subscribeValidator.EnsureValidAsync(request, ct);
        if (!sender.IsConfigured)
            throw new ConflictException("push.disabled", "Пуши на сервере не настроены (нет ключа VAPID).");

        var userId = RequireUser();
        var now = clock.UtcNow;
        // Адрес подписки уникален во всей системе: устройство, где раньше работал сотрудник другого ресторана,
        // переходит к новому — старая подписка (чужого ресторана) удаляется, а не перезаписывается.
        await db.PushSubscriptions.IgnoreQueryFilters()
            .Where(s => s.Endpoint == request.Endpoint && !db.PushSubscriptions.Any(own => own.Id == s.Id))
            .ExecuteDeleteAsync(ct);
        var existing = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == request.Endpoint, ct);
        if (existing is null)
            db.PushSubscriptions.Add(PushSubscription.Create(
                Guid.CreateVersion7(now), userId, request.Endpoint, request.Keys.P256dh, request.Keys.Auth, request.Language ?? "ru", now));
        else
            // То же устройство: обновить ключи, язык и владельца (вошёл другой сотрудник — пуши теперь ему).
            existing.Refresh(userId, request.Keys.P256dh, request.Keys.Auth, request.Language ?? existing.Language, now);
        await db.SaveChangesAsync(ct);
        return new PushStatusDto(true);
    }

    public async Task<PushStatusDto> UnsubscribeAsync(UnsubscribePushRequest request, CancellationToken ct)
    {
        await unsubscribeValidator.EnsureValidAsync(request, ct);
        var userId = RequireUser();
        await db.PushSubscriptions.Where(s => s.Endpoint == request.Endpoint && s.UserId == userId).ExecuteDeleteAsync(ct);
        return new PushStatusDto(false);
    }

    /// <summary>Проверочный пуш на устройства текущего пользователя: «Уведомления включены».</summary>
    public async Task<int> SendTestAsync(CancellationToken ct)
    {
        var userId = RequireUser();
        var subscriptions = await db.PushSubscriptions.Where(s => s.UserId == userId).ToListAsync(ct);
        var sent = 0;
        foreach (var subscription in subscriptions)
        {
            var en = subscription.Language == "en";
            var message = new PushMessage(
                en ? "Notifications are on" : "Уведомления включены",
                en ? "Mise will tell you when products spoil or dishes go into the stop list." : "Mise сообщит, когда продукты испортятся или блюда уйдут в стоп-лист.",
                "/alerts", "mise:test", AlertSeverity.Info, subscription.Language);
            var result = await sender.SendAsync(new PushEndpoint(subscription.Endpoint, subscription.P256dh, subscription.Auth), message, ct);
            if (result == PushSendResult.Sent)
            {
                subscription.MarkSent(clock.UtcNow);
                sent++;
            }
            else if (result == PushSendResult.Gone)
                db.PushSubscriptions.Remove(subscription);
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }

    private Guid RequireUser() =>
        currentUser.UserId ?? throw new UnauthorizedAccessException("Подписка на пуши — только после входа.");
}
