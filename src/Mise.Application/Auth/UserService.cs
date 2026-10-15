using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Mise.Application.Abstractions;
using Mise.Application.Common;
using Mise.Domain.Abstractions;
using Mise.Domain.Users;

namespace Mise.Application.Auth;

/// <summary>Управление пользователями (Manager). Смена роли/пароля и отключение отзывают refresh-токены пользователя.</summary>
public sealed class UserService(
    IMiseDbContext db,
    IClock clock,
    IPasswordHasher hasher,
    IValidator<CreateUserRequest> createValidator,
    IValidator<UpdateUserRequest> updateValidator,
    IValidator<ResetPasswordRequest> passwordValidator)
{
    public static class Errors
    {
        public const string DuplicateLogin = "user.duplicate_login";
        public const string LastManager = "user.last_manager";
    }

    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct) =>
        (await db.Users.AsNoTracking().OrderBy(u => u.Login).ToListAsync(ct)).Select(UserDto.From).ToList();

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        await createValidator.EnsureValidAsync(request, ct);
        var login = User.NormalizeLogin(request.Login);
        // Логин уникален во всей системе — занятый в другом ресторане тоже занят.
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Login == login, ct))
            throw new ConflictException(Errors.DuplicateLogin, $"Логин «{login}» уже занят.");

        var user = User.Create(Guid.CreateVersion7(clock.UtcNow), login, request.DisplayName, request.Role, hasher.Hash(request.Password), clock.UtcNow);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException)
        {
            throw new ConflictException(Errors.DuplicateLogin, $"Логин «{login}» уже занят.");
        }
        return UserDto.From(user);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        await updateValidator.EnsureValidAsync(request, ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("Пользователь", id);

        var losesManager = user is { Role: UserRole.Manager, IsActive: true } && (request.Role != UserRole.Manager || !request.IsActive);
        if (losesManager && !await db.Users.AnyAsync(u => u.Id != id && u.Role == UserRole.Manager && u.IsActive, ct))
            throw new ConflictException(Errors.LastManager, "Нельзя отключить или понизить последнего активного менеджера.");

        var accessChanged = user.Role != request.Role || user.IsActive != request.IsActive;
        user.Update(request.DisplayName, request.Role, request.IsActive);
        if (accessChanged)
            await AuthService.RevokeAllAsync(db, user.Id, clock.UtcNow, ct);
        await db.SaveChangesAsync(ct);
        return UserDto.From(user);
    }

    public async Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        await passwordValidator.EnsureValidAsync(request, ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("Пользователь", id);
        user.SetPasswordHash(hasher.Hash(request.Password));
        await AuthService.RevokeAllAsync(db, user.Id, clock.UtcNow, ct);
        await db.SaveChangesAsync(ct);
    }
}
