namespace Mise.Application.Common;

/// <summary>Ресурс не найден → 404.</summary>
public sealed class NotFoundException(string resource, object id)
    : Exception($"{resource} «{id}» не найден(а).")
{
    public string Resource { get; } = resource;
}

/// <summary>Не удалось подтвердить личность (вход, refresh) → 401 с кодом.</summary>
public sealed class UnauthorizedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
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

/// <summary>
/// Бизнес-отказ в выполнении корректного запроса (например, нехватка ингредиентов) → 422.
/// <see cref="Details"/> попадают в ProblemDetails как расширения (например, <c>missing</c>).
/// </summary>
public sealed class BusinessRuleException(string code, string message, IReadOnlyDictionary<string, object?>? details = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, object?> Details { get; } = details ?? new Dictionary<string, object?>();
}
