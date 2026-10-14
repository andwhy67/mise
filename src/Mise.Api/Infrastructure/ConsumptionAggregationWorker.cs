using Microsoft.Extensions.Options;
using Mise.Application.Abstractions;
using Mise.Application.Forecasting;

namespace Mise.Api.Infrastructure;

/// <summary>
/// Держит consumption_hourly тёплым (горячие предложения читают его без пересчёта); прогнозы и аналитика
/// дополнительно досчитывают агрегат перед чтением. Пересчёт идемпотентен и безопасен при нескольких инстансах.
/// </summary>
internal sealed class ConsumptionAggregationWorker(
    IServiceScopeFactory scopes,
    IOptions<ForecastOptions> options,
    ILogger<ConsumptionAggregationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.AggregationIntervalSeconds));
        try
        {
            do
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IConsumptionAggregator>().RefreshAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Consumption aggregation failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Штатная остановка.
        }
    }
}
