using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DocuWareSageConnector.Domain.Mapping;

public static class FieldFingerprint
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static string Compute(IReadOnlyDictionary<string, object?> fields)
    {
        var ordered = fields
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(pair => pair.Key, pair => Normalize(pair.Value), StringComparer.Ordinal);

        var json = JsonSerializer.Serialize(ordered, JsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static object? Normalize(object? value)
    {
        return value switch
        {
            null => null,
            DateTime dt => dt.ToString("O"),
            DateTimeOffset dto => dto.UtcDateTime.ToString("O"),
            decimal d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            double db => db.ToString(System.Globalization.CultureInfo.InvariantCulture),
            float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => value
        };
    }
}
