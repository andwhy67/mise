using System.Globalization;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Mise.Application.Catalog;
using Mise.Application.Stock;
using Mise.Application.Supplies;

namespace Mise.Application;

public static class DependencyInjection
{
    /// <summary>Регистрация use cases и валидаторов. Опции (<see cref="StockOptions"/>) привязывает хост.</summary>
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
        services.AddOptions<StockOptions>();
        return services;
    }
}
