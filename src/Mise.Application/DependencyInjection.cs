using Microsoft.Extensions.DependencyInjection;

namespace Mise.Application;

public static class DependencyInjection
{
    /// <summary>Регистрация use cases, валидаторов и доменных сервисов (наполняется с фазы 1).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services) => services;
}
