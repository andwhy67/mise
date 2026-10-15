using System.Security.Claims;
using Mise.Application.Abstractions;

namespace Mise.Api.Auth;

/// <summary>Ресторан запроса — из claim <c>rid</c> токена: все запросы к данным ограничены им (фильтр в DbContext).</summary>
internal sealed class TenantMiddleware(RequestDelegate next, ITenant tenant)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (Guid.TryParse(context.User.FindFirstValue(MiseClaims.RestaurantId), out var restaurantId))
        {
            using (tenant.Use(restaurantId))
                await next(context);
        }
        else
        {
            await next(context);
        }
    }
}
