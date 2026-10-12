namespace Mise.Application.Common;

/// <summary>Ресурс не найден → 404.</summary>
public sealed class NotFoundException(string resource, object id)
    : Exception($"{resource} «{id}» не найден(а).")
{
    public string Resource { get; } = resource;
}

/// <summary>Конфликт с текущим состоянием (дубликат, повтор ключа, ссылки) → 409.</summary>
public sealed class ConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Нарушение уникального ограничения в БД (Infrastructure переводит сюда ошибку провайдера).</summary>
public sealed class UniqueConstraintViolationException(string? constraintName, Exception inner)
    : Exception($"Нарушено ограничение уникальности {constraintName}.", inner)
{
    public string? ConstraintName { get; } = constraintName;
}
