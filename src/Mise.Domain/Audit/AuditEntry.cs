namespace Mise.Domain.Audit;

/// <summary>
/// Запись журнала аудита (PLAN §6): кто и когда выполнил критичное действие. Неизменяема.
/// </summary>
public sealed class AuditEntry
{
    private AuditEntry() { }

    public AuditEntry(Guid id, DateTimeOffset createdAt, string action, Guid? userId, string? login, string? entity, string? entityId,
        string? details, string? ip)
    {
        Id = id;
        CreatedAt = createdAt;
        Action = action;
        UserId = userId;
        Login = login;
        Entity = entity;
        EntityId = entityId;
        Details = details;
        Ip = ip;
    }

    public Guid Id { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    /// <summary>Например, supply.create, order.cancel, auth.login_failed.</summary>
    public string Action { get; private set; } = string.Empty;
    public Guid? UserId { get; private set; }
    /// <summary>Логин на момент действия (в т.ч. для неудачных входов, когда пользователя нет).</summary>
    public string? Login { get; private set; }
    public string? Entity { get; private set; }
    public string? EntityId { get; private set; }
    /// <summary>JSON с параметрами действия (маршрут, запрос) — без паролей и токенов.</summary>
    public string? Details { get; private set; }
    public string? Ip { get; private set; }
}
