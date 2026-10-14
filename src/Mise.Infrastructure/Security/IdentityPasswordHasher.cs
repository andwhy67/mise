using Microsoft.AspNetCore.Identity;
using Mise.Application.Auth;
using Mise.Domain.Users;

namespace Mise.Infrastructure.Security;

/// <summary>Хэширование паролей ASP.NET Identity (PBKDF2-HMAC-SHA512 с солью, формат v3).</summary>
internal sealed class IdentityPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(null!, password);

    public PasswordCheck Verify(string hash, string password)
    {
        try
        {
            return _hasher.VerifyHashedPassword(null!, hash, password) switch
            {
                PasswordVerificationResult.Success => PasswordCheck.Success,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
                _ => PasswordCheck.Failed,
            };
        }
        catch (FormatException)
        {
            // Повреждённый хэш или «пустышка» для несуществующего логина — просто неверный пароль.
            return PasswordCheck.Failed;
        }
    }
}
