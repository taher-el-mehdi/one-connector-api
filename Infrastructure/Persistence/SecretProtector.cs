using System.Security.Cryptography;
using System.Text;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class SecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static byte[] ReadKey(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                "ConnectorStore:SecretsKey is required. Set ConnectorStore__SecretsKey in .env to a base64-encoded 32-byte key.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configured.Trim());
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("ConnectorStore:SecretsKey must be base64.", exception);
        }

        if (key.Length != 32)
        {
            throw new InvalidOperationException("ConnectorStore:SecretsKey must decode to 32 bytes.");
        }

        return key;
    }

    public static string? Protect(string? plaintext, byte[] key)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return null;
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        var payload = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, NonceSize);
        cipher.CopyTo(payload, NonceSize + TagSize);
        return "v1." + Convert.ToBase64String(payload);
    }

    public static string Unprotect(string? stored, byte[] key)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return string.Empty;
        }

        const string prefix = "v1.";
        if (!stored.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A stored secret does not use the expected protection format.");
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(stored[prefix.Length..]);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("A stored secret could not be read.", exception);
        }

        if (payload.Length < NonceSize + TagSize)
        {
            throw new InvalidOperationException("A stored secret is incomplete.");
        }

        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipher = payload.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(
                "ConnectorStore:SecretsKey cannot decrypt a stored secret. Use the key that encrypted it.",
                exception);
        }

        return Encoding.UTF8.GetString(plain);
    }
}
