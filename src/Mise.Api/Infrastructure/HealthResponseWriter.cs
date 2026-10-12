using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Mise.Api.Infrastructure;

/// <summary>JSON-ответ /health: общий статус и статус каждой зависимости.</summary>
internal static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status = e.Value.Status.ToString(),
                    durationMs = Math.Round(e.Value.Duration.TotalMilliseconds, 1),
                    error = e.Value.Exception?.Message,
                }),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
