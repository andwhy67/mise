using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Mise.Application.Abstractions;
using Mise.Application.Audit;
using Mise.Application.Auth;
using Mise.Application.Common;
using Mise.Domain.Abstractions;
using Mise.Domain.Restaurants;
using Mise.Domain.Users;

namespace Mise.Application.Restaurants;

/// <summary>
/// Как человек попадает в систему: создаёт свой ресторан (и становится его менеджером) или входит в существующий
/// по приглашению менеджера. Аккаунта «без ресторана» не бывает — каждый сотрудник принадлежит ровно одному ресторану.
/// </summary>
public sealed class OnboardingService(
    IMiseDbContext db,
    IClock clock,
    ITenant tenant,
    IStockLocks locks,
    IPasswordHasher hasher,
    AuthService auth,
    AuditService audit,
    ICatalogTemplate catalog,
    IValidator<RegisterRestaurantRequest> registerValidator,
    IValidator<JoinRestaurantRequest> joinValidator)
{
    public static class Errors
    {
        public const string DuplicateLogin = "user.duplicate_login";
    }

    public static class Actions
    {
        public const string Register = "restaurant.register";
        public const string Join = "invite.accept";
    }

    public async Task<AuthSession> RegisterAsync(RegisterRestaurantRequest request, CancellationToken ct)
    {
        await registerValidator.EnsureValidAsync(request, ct);
        var login = User.NormalizeLogin(request.Login);
        await EnsureLoginIsFreeAsync(login, ct);

        var now = clock.UtcNow;
        var restaurant = new Restaurant(Guid.CreateVersion7(now), request.RestaurantName, now);
        var manager = User.Create(Guid.CreateVersion7(now), login, request.DisplayName.Trim(), UserRole.Manager, hasher.Hash(request.Password), now);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.Restaurants.Add(restaurant);
        // Всё дальнейшее — уже от имени нового ресторана: менеджер, справочник и журнал получают его id.
        using var scope = tenant.Use(restaurant.Id);
        db.Users.Add(manager);
        if (request.FillDemoCatalog)
            catalog.AddTo(db, now);
        await SaveAsync(login, ct);
        await audit.RecordAsync(Actions.Register, manager.Id, manager.Login, "Restaurant", restaurant.Id.ToString(),
            new { restaurant = restaurant.Name, demoCatalog = request.FillDemoCatalog }, ct);
        var session = await auth.StartSessionAsync(manager, restaurant, ct);
        await transaction.CommitAsync(ct);
        return session;
    }

    /// <summary>Куда ведёт код — до регистрации. Неизвестный, использованный или истёкший код — 404 без подробностей.</summary>
    public async Task<InvitePreviewDto> PreviewAsync(string code, CancellationToken ct)
    {
        var (invite, restaurant) = await FindUsableAsync(code, ct);
        return new InvitePreviewDto(restaurant.Name, invite.Role, invite.ExpiresAt);
    }

    public async Task<AuthSession> JoinAsync(JoinRestaurantRequest request, CancellationToken ct)
    {
        await joinValidator.EnsureValidAsync(request, ct);
        var login = User.NormalizeLogin(request.Login);
        await EnsureLoginIsFreeAsync(login, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var (invite, restaurant) = await FindUsableAsync(request.Code, ct, forUpdate: true);
        var now = clock.UtcNow;
        using var scope = tenant.Use(restaurant.Id);
        var user = User.Create(Guid.CreateVersion7(now), login, request.DisplayName.Trim(), invite.Role, hasher.Hash(request.Password), now);
        db.Users.Add(user);
        invite.Accept(user.Id, now);
        await SaveAsync(login, ct);
        await audit.RecordAsync(Actions.Join, user.Id, user.Login, "Invite", invite.Id.ToString(),
            new { role = invite.Role.ToString(), code = invite.DisplayCode }, ct);
        var session = await auth.StartSessionAsync(user, restaurant, ct);
        await transaction.CommitAsync(ct);
        return session;
    }

    /// <summary>
    /// Код ищется во всех ресторанах (сотрудник ещё не вошёл). При приёме — advisory-лок по коду в транзакции:
    /// два человека с одним кодом одновременно — войдёт только первый, второй увидит код уже использованным.
    /// </summary>
    private async Task<(Invite Invite, Restaurant Restaurant)> FindUsableAsync(string code, CancellationToken ct, bool forUpdate = false)
    {
        var normalized = Invite.Normalize(code);
        if (forUpdate)
            await locks.AcquireTransactionLockAsync("invite:" + normalized, ct);
        var found = await db.Invites.IgnoreQueryFilters().Where(i => i.Code == normalized)
            .Select(i => new { Invite = i, RestaurantId = EF.Property<Guid>(i, "RestaurantId") })
            .ToListAsync(ct);
        var row = found.SingleOrDefault();
        if (row is null || !row.Invite.IsUsableAt(clock.UtcNow))
            // 404 без подробностей: неизвестный, использованный и истёкший код неразличимы для подбора.
            throw new NotFoundException("Приглашение", normalized);
        var restaurant = await db.Restaurants.SingleAsync(r => r.Id == row.RestaurantId, ct);
        return (row.Invite, restaurant);
    }

    private async Task EnsureLoginIsFreeAsync(string login, CancellationToken ct)
    {
        // Логин уникален во всей системе: вход не спрашивает ресторан.
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Login == login, ct))
            throw new ConflictException(Errors.DuplicateLogin, $"Логин «{login}» уже занят — выберите другой.");
    }

    private async Task SaveAsync(string login, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException)
        {
            throw new ConflictException(Errors.DuplicateLogin, $"Логин «{login}» уже занят — выберите другой.");
        }
    }
}
