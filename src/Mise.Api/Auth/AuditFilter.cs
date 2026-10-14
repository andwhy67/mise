using System.Reflection;
using Mise.Application.Audit;

namespace Mise.Api.Auth;

/// <summary>
/// Аудит изменяющих эндпоинтов: после успешного ответа пишет кто, что и над какой сущностью (id — из маршрута или из ответа).
/// Ошибки (исключения, 4xx/5xx) не аудируются — действие не состоялось. Тело запроса не пишется: в нём могут быть пароли.
/// </summary>
internal static class AuditFilter
{
    public static TBuilder Audited<TBuilder>(this TBuilder builder, string action, string entity) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var result = await next(context);
            var http = context.HttpContext;
            // Results<A, B, …> — обёртка над фактическим результатом.
            var actual = result is INestedHttpResult nested ? nested.Result : result;
            var status = actual is IStatusCodeHttpResult { StatusCode: { } code } ? code : http.Response.StatusCode;
            if (status >= 400)
                return result;

            var entityId = http.Request.RouteValues.TryGetValue("id", out var routeId) && routeId is not null
                ? routeId.ToString()
                : IdOf(actual is IValueHttpResult value ? value.Value : actual);
            var details = new
            {
                method = http.Request.Method,
                path = http.Request.Path.Value,
                query = http.Request.QueryString.HasValue ? http.Request.QueryString.Value : null,
                idempotencyKey = http.Request.Headers.TryGetValue("Idempotency-Key", out var key) ? key.ToString() : null,
                status,
            };
            await http.RequestServices.GetRequiredService<AuditService>()
                .RecordAsync(action, entity, entityId, details, http.RequestAborted);
            return result;
        });

    private static string? IdOf(object? value)
    {
        if (value is null)
            return null;
        var type = value.GetType();
        var property = type.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)
                       ?? type.GetProperties(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(p => p.PropertyType == typeof(Guid));
        if (property is not null)
            return property.GetValue(value)?.ToString();
        // Вложенный объект (например, CancelOrderResult.Order).
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.GetValue(value))
            .Select(v => v?.GetType().GetProperty("Id")?.GetValue(v)?.ToString())
            .FirstOrDefault(id => id is not null);
    }
}
