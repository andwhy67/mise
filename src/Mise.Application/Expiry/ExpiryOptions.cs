namespace Mise.Application.Expiry;

/// <summary>Настройки мониторинга сроков (секция конфигурации "Expiry", PLAN §4.3).</summary>
public sealed class ExpiryOptions
{
    public const string SectionName = "Expiry";

    /// <summary>Запускать фоновый <c>ExpiryWorker</c> (в тестах выключен — тик вызывается напрямую).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Период тика, секунды.</summary>
    public int IntervalSeconds { get; set; } = 30;

    /// <summary>Списывать остаток просроченных партий автоматически (движение ExpiryWriteOff, статус WrittenOff).</summary>
    public bool AutoWriteOff { get; set; } = true;

    /// <summary>Сколько партий закрывать за один тик — остальные доберёт следующий.</summary>
    public int BatchSize { get; set; } = 500;

    public bool IsValid() => IntervalSeconds is >= 1 and <= 3600 && BatchSize is >= 1 and <= 10_000;
}
