using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Mise.Application.Simulator;

namespace Mise.Simulator.Api;

/// <summary>
/// Access-токен служебной учётной записи simulator (PLAN §8): вход по логину/паролю из env, повторный вход за минуту до истечения
/// и после 401. Один на процесс — обработчики HttpClient пересоздаются, кэш остаётся.
/// </summary>
public sealed class ApiTokenCache(IHttpClientFactory clients, IOptions<SimulatorOptions> options, TimeProvider time, ILogger<ApiTokenCache> logger)
{
    public const string AuthClient = "mise-auth";
    private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public bool Enabled => !string.IsNullOrWhiteSpace(options.Value.Password);

    public async Task<string?> GetAsync(CancellationToken ct)
    {
        if (!Enabled)
            return null;
        if (_token is not null && time.GetUtcNow() < _expiresAt - RenewBefore)
            return _token;

        await _gate.WaitAsync(ct);
        try
        {
            if (_token is not null && time.GetUtcNow() < _expiresAt - RenewBefore)
                return _token;

            var settings = options.Value;
            using var response = await clients.CreateClient(AuthClient)
                .PostAsJsonAsync("/api/v1/auth/login", new { login = settings.Login, password = settings.Password }, SimulatorProtocol.Json, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Simulator login failed: HTTP {Status}", (int)response.StatusCode);
                throw new HttpRequestException($"Вход симулятора не удался: HTTP {(int)response.StatusCode}", null, response.StatusCode);
            }
            var login = await response.Content.ReadFromJsonAsync<LoginResult>(SimulatorProtocol.Json, ct)
                ?? throw new HttpRequestException("Пустой ответ на вход симулятора");
            _token = login.AccessToken;
            // Часы API и симулятора могут расходиться: держим токен по своим часам (меньше срока access-токена API);
            // если API ответит 401 раньше — кэш сбросится и следующий запрос войдёт заново.
            _expiresAt = time.GetUtcNow() + CacheLifetime;
            logger.LogInformation("Simulator logged in");
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate() => _token = null;

    private sealed record LoginResult(string AccessToken);
}

/// <summary>Подставляет Bearer-токен симулятора; на 401 сбрасывает кэш (следующий запрос войдёт заново).</summary>
public sealed class ApiAuthHandler(ApiTokenCache tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (await tokens.GetAsync(cancellationToken) is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            tokens.Invalidate();
        return response;
    }
}
