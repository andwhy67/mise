using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Mise.Application.Abstractions;
using Mise.Application.Auth;
using Mise.Application.Common;
using Mise.Domain.Abstractions;
using Mise.Domain.Restaurants;

namespace Mise.Application.Restaurants;

/// <summary>Приглашения сотрудников — у менеджера своего ресторана: создать (роль, 7 дней), посмотреть, отозвать.</summary>
public sealed class InviteService(IMiseDbContext db, IClock clock, ICurrentUser currentUser, IValidator<CreateInviteRequest> validator)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);
    /// <summary>Показываем и использованные — менеджер видит, кто по какому коду пришёл; старше месяца — не нужны.</summary>
    private static readonly TimeSpan History = TimeSpan.FromDays(30);

    public async Task<InviteDto> CreateAsync(CreateInviteRequest request, CancellationToken ct)
    {
        await validator.EnsureValidAsync(request, ct);
        var now = clock.UtcNow;
        var userId = currentUser.UserId ?? throw new UnauthorizedException("auth.required", "Нужен вход.");
        var invite = Invite.Create(Guid.CreateVersion7(now), request.Role, userId, now, Lifetime);
        db.Invites.Add(invite);
        await db.SaveChangesAsync(ct);
        return InviteDto.From(invite, now, null);
    }

    public async Task<IReadOnlyList<InviteDto>> ListAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var since = now - History;
        var invites = await db.Invites.AsNoTracking().Where(i => i.CreatedAt >= since).OrderByDescending(i => i.CreatedAt).ToListAsync(ct);
        var acceptedIds = invites.Where(i => i.AcceptedBy != null).Select(i => i.AcceptedBy!.Value).ToList();
        var names = await db.Users.AsNoTracking().Where(u => acceptedIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return invites.Select(i => InviteDto.From(i, now, i.AcceptedBy is { } id ? names.GetValueOrDefault(id) : null)).ToList();
    }

    public async Task<InviteDto> RevokeAsync(Guid id, CancellationToken ct)
    {
        var invite = await db.Invites.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Приглашение", id);
        invite.Revoke(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return InviteDto.From(invite, clock.UtcNow, null);
    }
}
