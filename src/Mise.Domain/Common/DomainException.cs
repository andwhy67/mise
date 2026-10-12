namespace Mise.Domain.Common;

/// <summary>Нарушение доменного правила. <see cref="Code"/> — стабильный машинный код для клиента.</summary>
public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
