using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Mise.Application.Auth;
using Mise.Domain.Users;

namespace Mise.Api.Auth;

/// <summary>Имена claim-ов access-токена (без маппинга в длинные URI Microsoft).</summary>
public static class MiseClaims
{
    public const string UserId = JwtRegisteredClaimNames.Sub;
    public const string Login = "login";
    public const string Name = "name";
    public const string Role = "role";
    /// <summary>Ресторан — граница данных запроса.</summary>
    public const string RestaurantId = "rid";
}

/// <summary>Короткоживущий access JWT (HS256) — PLAN §6.</summary>
internal sealed class JwtTokenIssuer(IOptions<AuthOptions> options) : ITokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Issue(User user, Guid restaurantId, DateTimeOffset now)
    {
        var settings = options.Value;
        var expires = now.AddMinutes(settings.AccessTokenMinutes);
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(MiseClaims.UserId, user.Id.ToString()),
                new Claim(MiseClaims.Login, user.Login),
                new Claim(MiseClaims.Name, user.DisplayName),
                new Claim(MiseClaims.Role, user.Role.ToString()),
                new Claim(MiseClaims.RestaurantId, restaurantId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            ]),
            SigningCredentials = new SigningCredentials(SigningKey(settings), SecurityAlgorithms.HmacSha256),
        });
        return new AccessToken(token, expires);
    }

    public static SymmetricSecurityKey SigningKey(AuthOptions settings) => new(Encoding.UTF8.GetBytes(settings.SigningKey));
}
