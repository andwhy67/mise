using System.Security.Cryptography;
using System.Text.Json;

namespace Mise.Application.Common;

/// <summary>Хэш тела запроса для идемпотентности: отличает честный повтор от повторного использования ключа.</summary>
internal static class RequestHash
{
    public static string Compute<T>(T request) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));

    public static string? NormalizeKey(string? key) => string.IsNullOrWhiteSpace(key) ? null : key.Trim();
}
