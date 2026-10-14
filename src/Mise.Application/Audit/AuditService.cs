using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mise.Application.Abstractions;
using Mise.Application.Auth;
using Mise.Application.Common;
using Mise.Domain.Abstractions;
using Mise.Domain.Audit;

namespace Mise.Application.Audit;

public sealed record AuditEntryDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    string Action,
    Guid? UserId,
    string? Login,
    string? Entity,
    string? EntityId,
    JsonElement? Details,
    string? Ip);

public sealed record AuditQuery(string? Action = null, Guid? UserId = null, string? Entity = null, string? EntityId = null,
    DateTimeOffset? From = null, DateTimeOffset? To = null);

/// <summary>Журнал аудита (PLAN §6): кто, что, когда и откуда. Пароли и токены в детали не попадают.</summary>
public sealed class AuditService(IMiseDbContext db, IClock clock, ICurrentUser currentUser)
{
    public const int DetailsMaxLength = 4000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Действие текущего пользователя.</summary>
    public Task RecordAsync(string action, string? entity, string? entityId, object? details, CancellationToken ct) =>
        RecordAsync(action, currentUser.UserId, currentUser.Login, entity, entityId, details, ct);

    /// <summary>Действие с явным пользователем (вход, когда токена ещё нет).</summary>
    public async Task RecordAsync(string action, Guid? userId, string? login, string? entity, string? entityId, object? details,
        CancellationToken ct)
    {
        var json = details is null ? null : JsonSerializer.Serialize(details, Json);
        if (json is { Length: > DetailsMaxLength })
            json = JsonSerializer.Serialize(new { truncated = true }, Json);
        db.Audit.Add(new AuditEntry(Guid.CreateVersion7(clock.UtcNow), clock.UtcNow, action, userId, login, entity, entityId, json,
            currentUser.Ip));
        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<AuditEntryDto>> ListAsync(AuditQuery filter, PageRequest page, CancellationToken ct)
    {
        var query = db.Audit.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.Action))
            query = query.Where(a => a.Action == filter.Action);
        if (filter.UserId is { } userId)
            query = query.Where(a => a.UserId == userId);
        if (!string.IsNullOrWhiteSpace(filter.Entity))
            query = query.Where(a => a.Entity == filter.Entity);
        if (!string.IsNullOrWhiteSpace(filter.EntityId))
            query = query.Where(a => a.EntityId == filter.EntityId);
        if (filter.From is { } from)
            query = query.Where(a => a.CreatedAt >= from);
        if (filter.To is { } to)
            query = query.Where(a => a.CreatedAt < to);

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        var items = rows.Select(a => new AuditEntryDto(a.Id, a.CreatedAt, a.Action, a.UserId, a.Login, a.Entity, a.EntityId,
            a.Details is null ? null : JsonSerializer.Deserialize<JsonElement>(a.Details), a.Ip)).ToList();
        return new PagedResult<AuditEntryDto>(items, total, page.SafePage, page.SafePageSize);
    }
}
