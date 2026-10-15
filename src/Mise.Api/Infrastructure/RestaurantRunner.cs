using Microsoft.EntityFrameworkCore;
using Mise.Application.Abstractions;

namespace Mise.Api.Infrastructure;

/// <summary>
/// Фоновые задачи работают по ресторанам по очереди: свой scope и свой текущий ресторан на каждый —
/// сервисы те же, что в запросах, и видят только данные этого ресторана. Сбой одного ресторана не мешает остальным.
/// </summary>
internal sealed class RestaurantRunner(IServiceScopeFactory scopes, ITenant tenant, ILogger<RestaurantRunner> logger)
{
    public async Task ForEachAsync(Func<IServiceProvider, CancellationToken, Task> work, CancellationToken ct)
    {
        List<Guid> ids;
        await using (var scope = scopes.CreateAsyncScope())
            ids = await scope.ServiceProvider.GetRequiredService<IMiseDbContext>().Restaurants.AsNoTracking()
                .OrderBy(r => r.CreatedAt).Select(r => r.Id).ToListAsync(ct);

        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var _ = tenant.Use(id);
                await using var scope = scopes.CreateAsyncScope();
                await work(scope.ServiceProvider, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Background work failed for restaurant {RestaurantId}", id);
            }
        }
    }
}
