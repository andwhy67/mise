using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mise.Application.Abstractions;
using Mise.Application.Alerts;
using Mise.Domain.Abstractions;
using Mise.Domain.Alerts;
using Mise.Domain.Push;

namespace Mise.Application.Push;

/// <summary>
/// Рассылка пушей по новым алертам: кому — по роли (<see cref="PushAudience"/>), что — на языке устройства
/// (<see cref="PushTexts"/>). Мёртвые подписки (404/410 или серия ошибок) удаляются.
/// </summary>
public sealed class PushDispatcher(IMiseDbContext db, IPushSender sender, IClock clock, IOptions<PushOptions> options, ILogger<PushDispatcher> logger)
{
    public async Task<int> DispatchAsync(IReadOnlyList<AlertDto> alerts, CancellationToken ct)
    {
        if (!sender.IsConfigured)
            return 0;
        var targeted = alerts.Select(a => (Alert: a, Roles: PushAudience.Roles(a))).Where(x => x.Roles.Count > 0).ToList();
        if (targeted.Count == 0)
            return 0;

        var roles = targeted.SelectMany(x => x.Roles).Distinct().ToList();
        var subscriptions = await (
                from s in db.PushSubscriptions
                join u in db.Users on s.UserId equals u.Id
                where u.IsActive && roles.Contains(u.Role)
                select new { Subscription = s, u.Role })
            .ToListAsync(ct);

        var now = clock.UtcNow;
        var sent = 0;
        foreach (var target in subscriptions)
        {
            var subscription = target.Subscription;
            var messages = targeted
                .Where(x => x.Roles.Contains(target.Role))
                .Select(x => PushTexts.Build(x.Alert, subscription.Language, now))
                .OfType<PushMessage>()
                .ToList();
            if (messages.Count == 0)
                continue;
            // Пачка алертов (автосписание утром) не должна превращаться в десяток вибраций подряд.
            var max = options.Value.MaxPerDevice;
            if (messages.Count > max)
                messages = [.. messages.Take(max - 1), Summary(messages.Count - (max - 1), subscription.Language, messages.Max(m => m.Severity))];

            foreach (var message in messages)
            {
                var result = await sender.SendAsync(new PushEndpoint(subscription.Endpoint, subscription.P256dh, subscription.Auth), message, ct);
                if (result == PushSendResult.Sent)
                {
                    subscription.MarkSent(now);
                    sent++;
                    continue;
                }
                if (result == PushSendResult.Gone || subscription.MarkFailed())
                {
                    logger.LogInformation("Push subscription {Id} removed ({Result})", subscription.Id, result);
                    db.PushSubscriptions.Remove(subscription);
                    break;
                }
            }
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }

    private static PushMessage Summary(int count, string language, AlertSeverity severity) => language == "en"
        ? new PushMessage("Mise", $"And {count} more notifications — open Mise.", "/alerts", "mise:summary", severity, "en")
        : new PushMessage("Mise", $"И ещё уведомлений: {count} — откройте Mise.", "/alerts", "mise:summary", severity, "ru");
}
