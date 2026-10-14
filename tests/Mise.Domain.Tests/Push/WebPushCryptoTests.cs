using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mise.Infrastructure.Push;

namespace Mise.Domain.Tests.Push;

/// <summary>Шифрование пушей (RFC 8291, aes128gcm) и подпись VAPID (RFC 8292) — без сторонних библиотек.</summary>
public sealed class WebPushCryptoTests
{
    // RFC 8291, Appendix A.
    private const string Plaintext = "When I grow up, I want to be a watermelon";
    private const string AsPublic = "BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8";
    private const string AsPrivate = "yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw";
    private const string UaPublic = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";
    private const string UaPrivate = "q1dXpw3UpT5VOmu_cf_v6ih07Aems3njxI-JWgLcM94";
    private const string Salt = "DGv6ra1nlYgDCS1FRnbzlw";
    private const string AuthSecret = "BTBZMqHH6r4Tts7J_aSIgg";
    private const string ExpectedBody =
        "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_" +
        "yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN";

    [Fact]
    public void Encrypt_matches_rfc8291_test_vector()
    {
        using var sender = WebPushCrypto.ImportKey(Base64Url.Decode(AsPrivate), Base64Url.Decode(AsPublic));

        var body = WebPushCrypto.Encrypt(
            Encoding.UTF8.GetBytes(Plaintext), Base64Url.Decode(UaPublic), Base64Url.Decode(AuthSecret), sender, Base64Url.Decode(Salt));

        Assert.Equal(ExpectedBody, Base64Url.Encode(body));
    }

    [Fact]
    public void Encrypted_message_decrypts_with_the_subscriber_key()
    {
        using var subscriber = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var subscriberPublic = WebPushCrypto.ExportPublicKey(subscriber);
        var auth = RandomNumberGenerator.GetBytes(16);
        const string message = """{"title":"Шпинат испортился","url":"/stock"}""";

        var body = WebPushCrypto.Encrypt(Encoding.UTF8.GetBytes(message), subscriberPublic, auth);

        Assert.Equal(message, Encoding.UTF8.GetString(Decrypt(body, subscriber, subscriberPublic, auth)));
    }

    [Fact]
    public void Every_message_uses_a_fresh_key_and_salt()
    {
        using var subscriber = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var pub = WebPushCrypto.ExportPublicKey(subscriber);
        var auth = RandomNumberGenerator.GetBytes(16);

        var a = WebPushCrypto.Encrypt("x"u8.ToArray(), pub, auth);
        var b = WebPushCrypto.Encrypt("x"u8.ToArray(), pub, auth);

        Assert.NotEqual(a[..16], b[..16]);
        Assert.NotEqual(a[21..86], b[21..86]);
    }

    [Fact]
    public void Vapid_token_is_a_valid_es256_jwt_for_the_push_service_origin()
    {
        var keys = WebPushCrypto.GenerateVapidKeys();
        var now = new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

        var token = WebPushCrypto.CreateVapidToken("https://fcm.googleapis.com/fcm/send/abc", "mailto:ops@mise.local", keys.PrivateKey, now);

        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        using var header = JsonDocument.Parse(Base64Url.Decode(parts[0]));
        Assert.Equal("ES256", header.RootElement.GetProperty("alg").GetString());
        using var claims = JsonDocument.Parse(Base64Url.Decode(parts[1]));
        Assert.Equal("https://fcm.googleapis.com", claims.RootElement.GetProperty("aud").GetString());
        Assert.Equal("mailto:ops@mise.local", claims.RootElement.GetProperty("sub").GetString());
        Assert.Equal(now.AddHours(12).ToUnixTimeSeconds(), claims.RootElement.GetProperty("exp").GetInt64());

        using var verifier = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = PointOf(Base64Url.Decode(keys.PublicKey)) });
        Assert.True(verifier.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), Base64Url.Decode(parts[2]), HashAlgorithmName.SHA256));
    }

    /// <summary>Обратная операция (как в браузере): по ключу подписчика восстанавливаем текст.</summary>
    private static byte[] Decrypt(byte[] body, ECDiffieHellman subscriber, byte[] subscriberPublic, byte[] auth)
    {
        var salt = body[..16];
        var keyIdLength = body[20];
        var senderPublic = body[21..(21 + keyIdLength)];
        var cipher = body[(21 + keyIdLength)..];

        using var senderKey = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = PointOf(senderPublic) });
        var ecdh = subscriber.DeriveRawSecretAgreement(senderKey.PublicKey);
        var keyInfo = Encoding.ASCII.GetBytes("WebPush: info\0").Concat(subscriberPublic).Concat(senderPublic).ToArray();
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ecdh, 32, auth, keyInfo);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        var plain = new byte[cipher.Length - 16];
        using var aes = new AesGcm(cek, 16);
        aes.Decrypt(nonce, cipher[..^16], cipher[^16..], plain);
        Assert.Equal(0x02, plain[^1]);
        return plain[..^1];
    }

    private static ECPoint PointOf(byte[] uncompressed) => new() { X = uncompressed[1..33], Y = uncompressed[33..65] };
}
