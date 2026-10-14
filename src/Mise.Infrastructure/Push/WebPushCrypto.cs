using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mise.Infrastructure.Push;

/// <summary>Base64url без выравнивания — формат ключей и тел Web Push.</summary>
public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> data) => System.Buffers.Text.Base64Url.EncodeToString(data);

    public static byte[] Decode(string value) => System.Buffers.Text.Base64Url.DecodeFromChars(value.Trim());
}

/// <summary>Пара ключей VAPID: открытый (65 байт, несжатая точка P-256) и закрытый (32 байта) в base64url.</summary>
public sealed record VapidKeys(string PublicKey, string PrivateKey);

/// <summary>
/// Криптография Web Push только средствами .NET: шифрование тела (RFC 8291 + RFC 8188, aes128gcm)
/// и подпись VAPID (RFC 8292, ES256). Сторонний пакет WebPush не используется: он тянет устаревший
/// Portable.BouncyCastle с известными уязвимостями.
/// </summary>
public static class WebPushCrypto
{
    /// <summary>Размер записи aes128gcm: тело пуша укладывается в одну запись (лимит сервисов ~4 КБ).</summary>
    private const int RecordSize = 4096;
    private const int TagSize = 16;
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(12);

    /// <summary>Зашифровать сообщение для подписчика: свежие одноразовый ключ и соль на каждое сообщение.</summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] subscriberPublicKey, byte[] authSecret)
    {
        using var sender = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return Encrypt(plaintext, subscriberPublicKey, authSecret, sender, RandomNumberGenerator.GetBytes(16));
    }

    /// <summary>Детерминированный вариант — для проверки по тестовому вектору RFC 8291.</summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] subscriberPublicKey, byte[] authSecret, ECDiffieHellman sender, byte[] salt)
    {
        if (plaintext.Length + 1 + TagSize > RecordSize)
            throw new ArgumentException("Сообщение не помещается в одну запись Web Push.", nameof(plaintext));

        var senderPublic = ExportPublicKey(sender);
        using var subscriber = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = ToPoint(subscriberPublicKey) });
        var ecdhSecret = sender.DeriveRawSecretAgreement(subscriber.PublicKey);

        // RFC 8291 §3.4: IKM из общего секрета ECDH, auth-секрета подписки и обоих открытых ключей.
        var keyInfo = Concat(Encoding.ASCII.GetBytes("WebPush: info\0"), subscriberPublicKey, senderPublic);
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ecdhSecret, 32, authSecret, keyInfo);
        // RFC 8188 §2.2: ключ содержимого и nonce.
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        // Единственная (последняя) запись: текст + разделитель 0x02, без дополнения.
        var record = new byte[plaintext.Length + 1];
        plaintext.CopyTo(record, 0);
        record[^1] = 0x02;
        var cipher = new byte[record.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(cek, TagSize))
            aes.Encrypt(nonce, record, cipher, tag);

        // Заголовок: salt(16) | rs(4) | idlen(1) | keyid = открытый ключ отправителя.
        var header = new byte[16 + 4 + 1 + senderPublic.Length];
        salt.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16, 4), RecordSize);
        header[20] = (byte)senderPublic.Length;
        senderPublic.CopyTo(header, 21);
        return Concat(header, cipher, tag);
    }

    /// <summary>JWT VAPID для заголовка Authorization: aud — origin сервиса пушей, exp — 12 часов.</summary>
    public static string CreateVapidToken(string endpoint, string subject, string privateKey, DateTimeOffset now)
    {
        var audience = new Uri(endpoint).GetLeftPart(UriPartial.Authority);
        var header = Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(new { typ = "JWT", alg = "ES256" }));
        var claims = Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(new { aud = audience, exp = now.Add(TokenLifetime).ToUnixTimeSeconds(), sub = subject }));
        var signingInput = Encoding.ASCII.GetBytes($"{header}.{claims}");

        using var signer = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = Base64Url.Decode(privateKey) });
        // ES256 в JWT — подпись r||s по 32 байта (IEEE P1363), а не DER.
        var signature = signer.SignData(signingInput, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{header}.{claims}.{Base64Url.Encode(signature)}";
    }

    public static VapidKeys GenerateVapidKeys()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(includePrivateParameters: true);
        return new VapidKeys(Base64Url.Encode(Uncompressed(parameters.Q)), Base64Url.Encode(parameters.D!));
    }

    /// <summary>Открытый ключ из закрытого — чтобы в конфиге хватало одного секрета, а ключи не могли разойтись.</summary>
    public static string PublicKeyFor(string privateKey)
    {
        using var key = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = Base64Url.Decode(privateKey) });
        return Base64Url.Encode(Uncompressed(key.ExportParameters(includePrivateParameters: false).Q));
    }

    public static ECDiffieHellman ImportKey(byte[] privateKey, byte[] publicKey) =>
        ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = privateKey, Q = ToPoint(publicKey) });

    public static byte[] ExportPublicKey(ECDiffieHellman key) => Uncompressed(key.ExportParameters(includePrivateParameters: false).Q);

    private static byte[] Uncompressed(ECPoint point) => Concat([0x04], point.X!, point.Y!);

    private static ECPoint ToPoint(byte[] uncompressed)
    {
        if (uncompressed.Length != 65 || uncompressed[0] != 0x04)
            throw new ArgumentException("Ожидается несжатая точка P-256 (65 байт).", nameof(uncompressed));
        return new ECPoint { X = uncompressed[1..33], Y = uncompressed[33..65] };
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }
        return result;
    }
}
