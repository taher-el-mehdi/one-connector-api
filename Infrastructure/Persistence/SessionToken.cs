using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class SessionToken
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Issue(Guid userId, string username, DateTimeOffset expires, byte[] key)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Payload(userId.ToString("D"), username, expires.ToUnixTimeSeconds()), JsonOptions);
        var payloadText = Base64Url(payload);
        var signature = HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(payloadText));
        return payloadText + "." + Base64Url(signature);
    }

    public static Guid? ReadUserId(string token, byte[] key)
    {
        var parts = token.Split('.');
        if (parts.Length != 2)
        {
            return null;
        }

        byte[] actual;
        try
        {
            actual = FromBase64Url(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }

        var expected = HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(parts[0]));
        if (expected.Length != actual.Length || !CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            return null;
        }

        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(FromBase64Url(parts[0]), JsonOptions);
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }

        if (payload is null || !Guid.TryParse(payload.Sub, out var userId))
        {
            return null;
        }

        if (DateTimeOffset.FromUnixTimeSeconds(payload.Exp) <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        return userId;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }

    private sealed record Payload(string Sub, string Name, long Exp);
}
