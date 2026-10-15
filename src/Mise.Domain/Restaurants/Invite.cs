using System.Security.Cryptography;
using Mise.Domain.Common;
using Mise.Domain.Users;

namespace Mise.Domain.Restaurants;

/// <summary>
/// Приглашение в ресторан: менеджер выбирает роль, сотрудник регистрируется по коду и попадает ровно в этот
/// ресторан с этой ролью. Одноразовое, с ограниченным сроком; отозванное или использованное не работает.
/// </summary>
public sealed class Invite
{
    public static class Errors
    {
        public const string NotUsable = "invite.not_usable";
        public const string InvalidRole = "invite.invalid_role";
    }

    /// <summary>Без похожих символов (0/O, 1/I/L): код диктуют голосом и переписывают с экрана.</summary>
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int CodeLength = 8;

    private Invite() { }

    public static Invite Create(Guid id, UserRole role, Guid createdBy, DateTimeOffset now, TimeSpan lifetime)
    {
        if (role is not (UserRole.Manager or UserRole.Warehouse or UserRole.Chef))
            throw new DomainException(Errors.InvalidRole, "Пригласить можно менеджера, кладовщика или повара.");
        return new Invite
        {
            Id = id,
            Code = RandomCode(),
            Role = role,
            CreatedBy = createdBy,
            CreatedAt = now,
            ExpiresAt = now + lifetime,
        };
    }

    public Guid Id { get; private set; }
    /// <summary>Нормализованный код: 8 символов без дефиса.</summary>
    public string Code { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public Guid? AcceptedBy { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Как показывать и диктовать: «K7Q2-PXM4».</summary>
    public string DisplayCode => $"{Code[..4]}-{Code[4..]}";

    public bool IsUsableAt(DateTimeOffset now) => AcceptedAt is null && RevokedAt is null && now < ExpiresAt;

    public void Accept(Guid userId, DateTimeOffset now)
    {
        if (!IsUsableAt(now))
            throw new DomainException(Errors.NotUsable, "Приглашение уже использовано, отозвано или истекло.");
        AcceptedAt = now;
        AcceptedBy = userId;
    }

    public void Revoke(DateTimeOffset now)
    {
        if (AcceptedAt is null && RevokedAt is null)
            RevokedAt = now;
    }

    /// <summary>Код, как его ввели: регистр, пробелы и дефисы не важны.</summary>
    public static string Normalize(string typed) =>
        new((typed ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string RandomCode() =>
        new(Enumerable.Range(0, CodeLength).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray());
}
