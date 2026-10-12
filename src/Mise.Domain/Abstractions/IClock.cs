namespace Mise.Domain.Abstractions;

/// <summary>
/// Единственный источник текущего времени. Логика никогда не вызывает DateTime.UtcNow напрямую.
/// </summary>
public interface IClock
{
    /// <summary>Текущий момент в UTC (Offset == 0).</summary>
    DateTimeOffset UtcNow { get; }
}
