using Mise.Domain.Abstractions;

namespace Mise.Api.Endpoints;

internal static class SystemEndpoints
{
    public static RouteGroupBuilder MapSystemEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/ping", (IClock clock) => TypedResults.Ok(new PingResponse("ok", clock.UtcNow)))
            .WithName("Ping")
            .AllowAnonymous()
            .WithTags("System");
        return group;
    }

    internal sealed record PingResponse(string Status, DateTimeOffset ServerTimeUtc);
}
