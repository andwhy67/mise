using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Mise.Application.Common;
using Mise.Domain.Common;

namespace Mise.Api.Infrastructure;

/// <summary>
/// Исключения → RFC 7807 ProblemDetails:
/// валидация → 400, не найдено → 404, конфликт → 409, нарушение доменного правила → 422, прочее → 500 без деталей.
/// </summary>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var problem = exception switch
        {
            ValidationException validation => ToValidationProblem(validation),
            BadHttpRequestException badRequest => Problem(badRequest.StatusCode, "Некорректный запрос", DescribeBadRequest(badRequest), "request.malformed"),
            NotFoundException notFound => Problem(StatusCodes.Status404NotFound, "Не найдено", notFound.Message, "not_found"),
            ConflictException conflict => Problem(StatusCodes.Status409Conflict, "Конфликт", conflict.Message, conflict.Code),
            UniqueConstraintViolationException => Problem(StatusCodes.Status409Conflict, "Конфликт",
                "Запись с такими данными уже существует.", "unique_violation"),
            BusinessRuleException rule => WithDetails(
                Problem(StatusCodes.Status422UnprocessableEntity, "Операция невозможна", rule.Message, rule.Code), rule.Details),
            DomainException domain => Problem(StatusCodes.Status422UnprocessableEntity, "Операция невозможна", domain.Message, domain.Code),
            _ => null,
        };

        if (problem is null)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Внутренняя ошибка сервера",
                Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.1",
            };
        }
        else
        {
            logger.LogInformation("Request {Method} {Path} rejected with {Status}: {Detail}",
                context.Request.Method, context.Request.Path, problem.Status, problem.Detail ?? problem.Title);
        }

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = problem,
        });
    }

    private static ProblemDetails Problem(int status, string title, string detail, string code) => new()
    {
        Status = status,
        Title = title,
        Detail = detail,
        Extensions = { ["code"] = code },
    };

    private static ProblemDetails WithDetails(ProblemDetails problem, IReadOnlyDictionary<string, object?> details)
    {
        foreach (var (key, value) in details)
            problem.Extensions[key] = value;
        return problem;
    }

    private static ValidationProblemDetails ToValidationProblem(ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(e => ToCamelCasePath(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
        return new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Ошибка валидации",
            Detail = "Проверьте поля запроса.",
            Extensions = { ["code"] = "validation" },
        };
    }

    private static string DescribeBadRequest(BadHttpRequestException exception) => exception.InnerException switch
    {
        JsonException { Path: { Length: > 0 } path } => $"Некорректное значение в JSON по пути {path}.",
        JsonException => "Тело запроса — некорректный JSON.",
        _ => exception.Message,
    };

    /// <summary>"Items[0].ProductId" → "items[0].productId" — как поля в JSON.</summary>
    internal static string ToCamelCasePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        var builder = new StringBuilder(path.Length);
        var segmentStart = true;
        foreach (var c in path)
        {
            builder.Append(segmentStart ? char.ToLowerInvariant(c) : c);
            segmentStart = c == '.';
        }
        return builder.ToString();
    }
}
