using FluentValidation;
using Mise.Domain.Push;

namespace Mise.Application.Push;

/// <summary>
/// Настройки Web Push (секция "Push", env Push__*). Ключ VAPID — только из env; открытый ключ выводится из закрытого.
/// Сгенерировать пару: <c>dotnet run --project src/Mise.Api -- --generate-vapid-keys</c>.
/// </summary>
public sealed class PushOptions
{
    public const string SectionName = "Push";

    /// <summary>Пуши включены, только если задан закрытый ключ VAPID.</summary>
    public bool Enabled => !string.IsNullOrWhiteSpace(PrivateKey);

    /// <summary>Контакт для сервисов пушей (RFC 8292): mailto: или https:.</summary>
    public string Subject { get; set; } = "mailto:admin@mise.local";

    public string? PrivateKey { get; set; }

    /// <summary>
    /// Куда сервер может отправлять пуши. Адрес подписки присылает браузер, поэтому без списка это была бы
    /// возможность заставить сервер стучаться во внутреннюю сеть (SSRF). Суффиксы доменов, только https.
    /// </summary>
    public string[] AllowedHosts { get; set; } =
        ["fcm.googleapis.com", "push.services.mozilla.com", "push.apple.com", "notify.windows.com", "wns.windows.com"];

    /// <summary>Сколько пушей одному устройству за одну рассылку; остальное — одним «и ещё N».</summary>
    public int MaxPerDevice { get; set; } = 4;

    public bool IsValid() =>
        (Subject.StartsWith("mailto:", StringComparison.Ordinal) || Subject.StartsWith("https://", StringComparison.Ordinal))
        && AllowedHosts.Length > 0 && MaxPerDevice is >= 1 and <= 20;

    public bool IsAllowedEndpoint(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.HostNameType == UriHostNameType.Dns
        && AllowedHosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
                                    || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Что нужно браузеру, чтобы подписаться: включены ли пуши и открытый ключ VAPID.</summary>
public sealed record PushConfigDto(bool Enabled, string? PublicKey);

public sealed record PushKeys(string P256dh, string Auth);

/// <summary>Подписка из PushSubscription.toJSON() браузера + язык интерфейса.</summary>
public sealed record SubscribePushRequest(string Endpoint, PushKeys Keys, string? Language);

public sealed record UnsubscribePushRequest(string Endpoint);

public sealed record PushStatusDto(bool Subscribed);

public sealed record PushEndpoint(string Endpoint, string P256dh, string Auth);

public enum PushSendResult
{
    Sent,
    /// <summary>Подписки больше нет (404/410): удалить.</summary>
    Gone,
    /// <summary>Временная ошибка: попробуем со следующим уведомлением.</summary>
    Failed,
}

/// <summary>Отправка одного пуша (шифрование RFC 8291 + VAPID) — реализация в Infrastructure.</summary>
public interface IPushSender
{
    bool IsConfigured { get; }
    string? PublicKey { get; }
    Task<PushSendResult> SendAsync(PushEndpoint target, PushMessage message, CancellationToken ct);
}

internal sealed class SubscribePushRequestValidator : AbstractValidator<SubscribePushRequest>
{
    public SubscribePushRequestValidator(Microsoft.Extensions.Options.IOptions<PushOptions> options)
    {
        RuleFor(x => x.Endpoint).NotEmpty().MaximumLength(PushSubscription.EndpointMaxLength)
            .Must(endpoint => options.Value.IsAllowedEndpoint(endpoint))
            .WithErrorCode("push.endpoint_not_allowed")
            .WithMessage("Адрес подписки не похож на сервис пушей браузера.");
        RuleFor(x => x.Keys).NotNull();
        RuleFor(x => x.Keys.P256dh).NotEmpty().MaximumLength(PushSubscription.KeyMaxLength).Matches("^[A-Za-z0-9_-]+=*$").When(x => x.Keys is not null);
        RuleFor(x => x.Keys.Auth).NotEmpty().MaximumLength(PushSubscription.KeyMaxLength).Matches("^[A-Za-z0-9_-]+=*$").When(x => x.Keys is not null);
        RuleFor(x => x.Language).MaximumLength(16);
    }
}

internal sealed class UnsubscribePushRequestValidator : AbstractValidator<UnsubscribePushRequest>
{
    public UnsubscribePushRequestValidator() => RuleFor(x => x.Endpoint).NotEmpty().MaximumLength(PushSubscription.EndpointMaxLength);
}
