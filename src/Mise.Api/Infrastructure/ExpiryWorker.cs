using Microsoft.Extensions.Options;
using Mise.Application.Expiry;

namespace Mise.Api.Infrastructure;

/// <summary>
/// Фоновый мониторинг сроков (PLAN §4.3): тик сразу после старта и далее каждые <see cref="ExpiryOptions.IntervalSeconds"/>.
/// Ошибка тика логируется и не останавливает воркер; параллельные инстансы разводит advisory lock внутри сервиса.
/// </summary>
internal sealed class ExpiryWorker(IServiceScopeFactory scopes, IOptions<ExpiryOptions> options, ILogger<ExpiryWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Expiry worker is disabled");
            return;
        }

        logger.LogInformation("Expiry worker started: every {Interval}s, auto write-off {AutoWriteOff}",
            settings.IntervalSeconds, settings.AutoWriteOff);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.IntervalSeconds));
        try
        {
            do
            {
                await TickAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Штатная остановка.
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<ExpiryMonitorService>().RunOnceAsync(ct);
            if (result.Skipped)
                logger.LogDebug("Expiry tick skipped: another instance holds the lock");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Expiry tick failed");
        }
    }
}
