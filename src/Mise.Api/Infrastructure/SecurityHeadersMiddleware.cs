namespace Mise.Api.Infrastructure;

/// <summary>
/// Заголовки безопасности (PLAN §6): API отдаёт только JSON — запрещаем всё, кроме него; Swagger UI получает свой CSP.
/// HSTS включается отдельно (UseHsts вне Development).
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string ApiCsp = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
    private const string SwaggerCsp =
        "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers.ContentSecurityPolicy = context.Request.Path.StartsWithSegments("/swagger") ? SwaggerCsp : ApiCsp;
        return next(context);
    }
}
