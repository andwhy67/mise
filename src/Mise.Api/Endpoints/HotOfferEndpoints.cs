using Mise.Application.HotOffers;

namespace Mise.Api.Endpoints;

internal static class HotOfferEndpoints
{
    public static RouteGroupBuilder MapHotOfferEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/hot-offers", (HotOfferService service, CancellationToken ct, int? limit = null, string? scorer = null) =>
                service.GetAsync(limit, scorer, ct))
            .WithTags("Горячие предложения")
            .WithSummary("Что продавать в первую очередь: топ блюд со score 0–100, причинами и объяснением")
            .WithDescription(
                "Блюда, которые помогут продать сырьё с истекающим сроком (PLAN §4.4, docs/hot-offers.md). " +
                "limit — 1…20 (по умолчанию из настроек), scorer — v1 (дословно §4.4) или v2 (учитывает, что обычный " +
                "спрос и так разберёт часть партии до истечения). Обновления — событие HotOffersChanged в /hubs/stock.");
        api.MapGet("/hot-offers/menu", (HotOfferService service, CancellationToken ct, string? scorer = null) => service.GetMenuAsync(scorer, ct))
            .WithTags("Горячие предложения")
            .WithSummary("Всё доступное меню по порядку «что предлагать»: спасают продукты → разгружают склад → остальное")
            .WithDescription("Группы: Saves (горячие предложения с причинами), Overstock (скоропортящегося запаса на OverstockDays+ дней спроса), Regular (по числу порций).");
        return api;
    }
}
