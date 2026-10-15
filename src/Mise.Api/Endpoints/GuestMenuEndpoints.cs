using Microsoft.AspNetCore.Mvc;
using Mise.Api.Auth;
using Mise.Application.GuestMenu;

namespace Mise.Api.Endpoints;

/// <summary>
/// QR-меню. Публичная часть (<c>/public/menu/{key}</c>) — без входа: ресторан определяется ключом из QR.
/// Настройки (<c>/guest-menu</c>) — у менеджера.
/// </summary>
internal static class GuestMenuEndpoints
{
    public static RouteGroupBuilder MapGuestMenuEndpoints(this RouteGroupBuilder api)
    {
        var guest = api.MapGroup("/public/menu/{key}").WithTags("QR-меню (гость)").AllowAnonymous();

        guest.MapGet("/", (string key, GuestMenuService service, CancellationToken ct) => service.GetMenuAsync(key, ct))
            .WithSummary("Меню для гостя: блюда, цены, «есть / можно с изменениями / нет», рекомендации шефа")
            .WithDescription("Остатки склада наружу не отдаются — только состояние блюда и какие ингредиенты можно убрать или заменить.");
        guest.MapPost("/orders", PlaceAsync)
            .WithSummary("Заказ гостя: те же проверки, что у повара; сразу на экран кухни")
            .RequireRateLimiting(RateLimitingOptions.GuestOrderPolicy)
            .WithDescription($"Не больше {GuestMenuService.MaxPortions} порций. При нехватке — 422 guest.unavailable со списком блюд (без граммов).")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        guest.MapGet("/orders/{token}", (string key, string token, GuestMenuService service, CancellationToken ct) =>
                service.GetOrderAsync(key, token, ct))
            .WithSummary("Статус заказа гостя по секретной ссылке");

        var settings = api.MapGroup("/guest-menu").WithTags("QR-меню (настройки)").RequireAuthorization(AuthPolicies.Manager);
        settings.MapGet("/", (GuestMenuService service, CancellationToken ct) => service.GetSettingsAsync(ct))
            .WithSummary("Ключ меню, приём заказов, число столов (ключ создаётся при первом обращении)");
        settings.MapPut("/", (UpdateGuestMenuRequest request, GuestMenuService service, CancellationToken ct) =>
                service.UpdateSettingsAsync(request, ct))
            .WithSummary("Включить/выключить заказы гостей, число столов")
            .Audited("guest_menu.update", "Restaurant");
        settings.MapPost("/regenerate", (GuestMenuService service, CancellationToken ct) => service.RegenerateKeyAsync(ct))
            .WithSummary("Новый ключ меню — старые QR перестают работать")
            .Audited("guest_menu.regenerate", "Restaurant");

        return api;
    }

    private static async Task<GuestOrderDto> PlaceAsync(
        string key,
        PlaceGuestOrderRequest request,
        [FromHeader(Name = SupplyEndpoints.IdempotencyHeader)] string? idempotencyKey,
        GuestMenuService service,
        CancellationToken ct)
    {
        var safeKey = idempotencyKey is { Length: > 0 and <= SupplyEndpoints.IdempotencyKeyMaxLength } ? idempotencyKey : null;
        return await service.PlaceOrderAsync(key, request, safeKey, ct);
    }
}
