using Mise.Application.Analytics;
using Mise.Application.Forecasting;

namespace Mise.Api.Endpoints;

internal static class AnalyticsEndpoints
{
    public static RouteGroupBuilder MapForecastEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/stoplist/forecast", (ForecastService service, CancellationToken ct, int? horizonHours = null) =>
                service.GetStopListForecastAsync(horizonHours, ct))
            .WithTags("Меню и стоп-лист")
            .WithSummary("Прогноз стоп-листа: какие доступные блюда и когда уйдут в стоп")
            .WithDescription(
                "Ожидаемый расход (EWMA по consumption_hourly, профиль «час дня» при ≥ 3 днях истории) списывает партии по FEFO, " +
                "истёкшее пропадает. eta — когда остатка ингредиента станет меньше порции; cause — Consumption или Expiry; " +
                "confidence — по объёму истории. horizonHours: 1…72, по умолчанию 24.");

        api.MapGet("/supply-suggestions", (ForecastService service, CancellationToken ct, int? horizonHours = null) =>
                service.GetSupplySuggestionsAsync(horizonHours, ct))
            .WithTags("Поставки")
            .WithSummary("Что и сколько заказать: рекомендуемая партия, срочность, причины")
            .WithDescription(
                "need = расход за горизонт + страховой запас − остаток, который доживёт; ограничение — сколько успеем использовать " +
                "до порчи; округление до упаковки. horizonHours: по умолчанию 72.");

        return api;
    }

    public static RouteGroupBuilder MapAnalyticsEndpoints(this RouteGroupBuilder api)
    {
        var analytics = api.MapGroup("/analytics").WithTags("Аналитика");

        analytics.MapGet("/writeoffs", async (AnalyticsService service, CancellationToken ct, DateTimeOffset? from = null, DateTimeOffset? to = null) =>
                await service.GetWriteOffsAsync(service.ResolvePeriod(from, to), ct))
            .WithSummary("Списания по дням, продуктам и причинам (по сроку / вручную); период по умолчанию — 7 дней");

        analytics.MapGet("/deficit", async (AnalyticsService service, CancellationToken ct, DateTimeOffset? from = null, DateTimeOffset? to = null) =>
                await service.GetDeficitAsync(service.ResolvePeriod(from, to), ct))
            .WithSummary("Дефицитные ингредиенты: попадания блюд в стоп из-за продукта, минуты в стопе, доля времени ниже минимума");

        analytics.MapGet("/consumption", async (AnalyticsService service, CancellationToken ct, DateTimeOffset? from = null,
                DateTimeOffset? to = null, ConsumptionBucket bucket = ConsumptionBucket.Hour, Guid? productId = null, int limit = 10) =>
                await service.GetConsumptionAsync(service.ResolvePeriod(from, to), bucket, productId, limit, ct))
            .WithSummary("Расход по продуктам: ряды по часам или суткам (Москва), топ по объёму");

        analytics.MapGet("/kpi", async (AnalyticsService service, CancellationToken ct, DateTimeOffset? from = null, DateTimeOffset? to = null) =>
                await service.GetKpiAsync(service.ResolvePeriod(from, to), ct))
            .WithSummary("KPI периода (заказы, выручка, доля списаний, время в стопе) и плитки «сейчас/сегодня» для дашборда");

        return api;
    }
}
