using Mise.Application.Auth;
using Mise.Domain.Users;

namespace Mise.Infrastructure.Seed;

/// <summary>
/// «Кто действует» вне HTTP-запроса — для генератора истории: склад принимает поставки, повар ведёт заказы.
/// Регистрируется только в режиме генерации; в API текущий пользователь берётся из токена.
/// </summary>
public sealed class ScriptedCurrentUser : ICurrentUser
{
    public ScriptedCurrentUser() => Instance = this;

    /// <summary>Есть, только если генератор запущен в своём режиме (иначе журнал пишется без автора-сценария).</summary>
    public static ScriptedCurrentUser? Instance { get; private set; }

    public Guid? UserId { get; private set; }
    public string? Login { get; private set; }
    public UserRole? Role { get; private set; }
    public string? Ip => "127.0.0.1";

    public void Set(Guid userId, string login, UserRole role)
    {
        UserId = userId;
        Login = login;
        Role = role;
    }
}
