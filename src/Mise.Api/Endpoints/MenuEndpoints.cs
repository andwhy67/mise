using Mise.Application.Common;
using Mise.Application.Menu;

namespace Mise.Api.Endpoints;

internal static class MenuEndpoints
{
    public static RouteGroupBuilder MapMenuEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/menu/availability", (MenuQueryService service, CancellationToken ct) => service.GetAvailabilityAsync(ct))
            .WithTags("Меню и стоп-лист")
            .WithSummary("Все блюда: сколько порций можно приготовить, ограничивающий продукт, статус");

        api.MapGet("/stoplist", (MenuQueryService service, CancellationToken ct) => service.GetStopListAsync(ct))
            .WithTags("Меню и стоп-лист")
            .WithSummary("Блюда в стоп-листе: причина (нехватка / вручную), ограничивающий продукт, с какого момента");

        api.MapGet("/stoplist/history", (MenuQueryService service, CancellationToken ct, Guid? dishId = null, int page = 1, int pageSize = 50) =>
                service.GetHistoryAsync(dishId, new PageRequest(page, pageSize), ct))
            .WithTags("Меню и стоп-лист")
            .WithSummary("История интервалов стоп-листа (новые сверху)");

        return api;
    }
}
