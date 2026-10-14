namespace Mise.Application.Abstractions;

/// <summary>Агрегатор расхода consumption_hourly (идемпотентный инкрементальный пересчёт из журнала движений).</summary>
public interface IConsumptionAggregator
{
    /// <summary>Досчитывает часы с последнего записанного; возвращает число записанных строк.</summary>
    Task<int> RefreshAsync(CancellationToken ct);
}
