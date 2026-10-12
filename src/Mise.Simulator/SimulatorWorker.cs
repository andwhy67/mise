namespace Mise.Simulator;

/// <summary>
/// Генератор поставок и заказов (PLAN §8). Пока скелет: реальная логика появится в фазе 4.
/// </summary>
public sealed class SimulatorWorker(ILogger<SimulatorWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Simulator started in idle mode");
        using var timer = new PeriodicTimer(IdleInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            logger.LogDebug("Simulator idle tick");
        }
    }
}
