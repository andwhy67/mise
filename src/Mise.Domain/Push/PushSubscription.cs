namespace Mise.Domain.Push;

/// <summary>
/// Подписка браузера на пуши (Web Push): адрес сервиса пушей и ключи шифрования устройства.
/// Одно устройство (endpoint) — одна запись; при входе другим пользователем подписка переходит к нему.
/// </summary>
public sealed class PushSubscription
{
    public const int EndpointMaxLength = 1000;
    public const int KeyMaxLength = 200;
    /// <summary>После стольких неудач подряд (не 404/410) подписка считается мёртвой.</summary>
    public const int MaxFailures = 5;

    private PushSubscription() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Endpoint { get; private set; } = string.Empty;
    /// <summary>Открытый ключ устройства (P-256, base64url).</summary>
    public string P256dh { get; private set; } = string.Empty;
    /// <summary>Секрет аутентификации (16 байт, base64url).</summary>
    public string Auth { get; private set; } = string.Empty;
    /// <summary>Язык интерфейса на устройстве — на нём пишется текст пуша.</summary>
    public string Language { get; private set; } = "ru";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? LastSentAt { get; private set; }
    public int FailureCount { get; private set; }

    public static PushSubscription Create(Guid id, Guid userId, string endpoint, string p256dh, string auth, string language, DateTimeOffset now) => new()
    {
        Id = id,
        UserId = userId,
        Endpoint = endpoint,
        P256dh = p256dh,
        Auth = auth,
        Language = NormalizeLanguage(language),
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>Повторная подписка того же устройства: ключи могли смениться, вошёл другой пользователь, сменился язык.</summary>
    public void Refresh(Guid userId, string p256dh, string auth, string language, DateTimeOffset now)
    {
        UserId = userId;
        P256dh = p256dh;
        Auth = auth;
        Language = NormalizeLanguage(language);
        FailureCount = 0;
        UpdatedAt = now;
    }

    public void MarkSent(DateTimeOffset now)
    {
        LastSentAt = now;
        FailureCount = 0;
    }

    /// <summary>Временная ошибка сервиса пушей; true — пора удалить подписку.</summary>
    public bool MarkFailed() => ++FailureCount >= MaxFailures;

    /// <summary>Поддерживаемые языки текстов пушей; остальное — русский.</summary>
    public static string NormalizeLanguage(string? language) =>
        (language ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            var l when l.StartsWith("en", StringComparison.Ordinal) => "en",
            _ => "ru",
        };
}
