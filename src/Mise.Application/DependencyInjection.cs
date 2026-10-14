using System.Globalization;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Mise.Application.Alerts;
using Mise.Application.Analytics;
using Mise.Application.Audit;
using Mise.Application.Auth;
using Mise.Application.Catalog;
using Mise.Application.Expiry;
using Mise.Application.Forecasting;
using Mise.Application.HotOffers;
using Mise.Application.Menu;
using Mise.Application.Orders;
using Mise.Application.Push;
using Mise.Application.Realtime;
using Mise.Application.Simulator;
using Mise.Application.Stock;
using Mise.Application.Supplies;
using Mise.Domain.HotOffers;

namespace Mise.Application;

public static class DependencyInjection
{
    /// <summary>Регистрация use cases и валидаторов. <see cref="IRealtimeNotifier"/> и <see cref="ISimulatorControlStore"/> регистрирует хост/инфраструктура. Опции (<see cref="StockOptions"/>, <see cref="ExpiryOptions"/>) привязывает хост.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Сообщения FluentValidation — на русском, независимо от культуры сервера.
        ValidatorOptions.Global.LanguageManager.Culture = CultureInfo.GetCultureInfo("ru");
        // Внутри одного правила — до первой ошибки: Must(...) не должен выполняться на null после NotEmpty.
        ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Stop;
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        services.AddScoped<ProductService>();
        services.AddScoped<DishService>();
        services.AddScoped<SupplyService>();
        services.AddScoped<StockQueryService>();
        services.AddScoped<OrderService>();
        services.AddScoped<MenuQueryService>();
        services.AddScoped<StopListService>();
        services.AddScoped<WriteOffService>();
        services.AddScoped<AlertService>();
        services.AddScoped<ExpiryMonitorService>();
        services.AddScoped<RealtimePayloadBuilder>();
        services.AddScoped<SimulatorService>();
        services.AddScoped<HotOfferService>();
        services.AddScoped<ConsumptionRates>();
        services.AddScoped<ForecastService>();
        services.AddScoped<AnalyticsService>();
        services.AddOptions<ForecastOptions>();
        services.AddScoped<AuthService>();
        services.AddScoped<UserService>();
        services.AddScoped<AuditService>();
        services.AddScoped<PushService>();
        services.AddScoped<PushDispatcher>();
        services.AddOptions<AuthOptions>();
        services.AddSingleton<HotOfferState>();
        services.AddSingleton<IHotOfferScorer, HotOfferScorerV1>();
        services.AddSingleton<IHotOfferScorer, HotOfferScorerV2>();
        services.AddOptions<HotOfferOptions>();
        services.AddOptions<StockOptions>();
        services.AddOptions<ExpiryOptions>();
        return services;
    }
}
