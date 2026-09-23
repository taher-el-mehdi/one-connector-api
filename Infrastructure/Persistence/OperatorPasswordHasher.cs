using Microsoft.AspNetCore.Identity;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class OperatorPasswordHasher
{
    private static readonly PasswordHasher<object> Hasher = new();
    private static readonly string DummyHash = Hasher.HashPassword(new object(), "connector-store-dummy");

    public static string Hash(string password) => Hasher.HashPassword(new object(), password);

    public static bool Verify(string hash, string password) =>
        Hasher.VerifyHashedPassword(new object(), hash, password) != PasswordVerificationResult.Failed;

    public static void VerifyDummy(string password) => Verify(DummyHash, password.Length == 0 ? " " : password);
}
