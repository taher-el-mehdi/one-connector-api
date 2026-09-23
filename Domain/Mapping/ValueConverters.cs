using System.Globalization;
using System.Text.RegularExpressions;
using DocuWareSageConnector.Domain.Enums;

namespace DocuWareSageConnector.Domain.Mapping;

public static class ValueConverters
{
    private static readonly Regex MicrosoftJsonDate =
        new(@"^/Date\((-?\d+)([+-]\d{4})?\)/$", RegexOptions.Compiled);

    public static object? FormatForDocuWare(object? value, IndexFieldType type)
    {
        if (value is null)
        {
            return null;
        }

        return type switch
        {
            IndexFieldType.Text => ToText(value),
            IndexFieldType.Numeric => ToNumeric(value),
            IndexFieldType.DateTime => ToDateTime(value),
            _ => value
        };
    }

    public static object? FormatForSage(object? value, IndexFieldType type, string? sageColumn)
    {
        if (value is null)
        {
            return null;
        }

        if (type == IndexFieldType.DateTime)
        {
            return ToDateTime(value);
        }

        var formatted = FormatForDocuWare(value, type);
        if (formatted is null)
        {
            return null;
        }

        if (string.Equals(sageColumn, "CG_NumPrinc", StringComparison.OrdinalIgnoreCase)
            && formatted is IConvertible)
        {
            if (formatted is int i)
            {
                return i.ToString(CultureInfo.InvariantCulture);
            }

            if (formatted is long l)
            {
                return l.ToString(CultureInfo.InvariantCulture);
            }

            if (formatted is decimal d && d == decimal.Truncate(d))
            {
                return decimal.Truncate(d).ToString(CultureInfo.InvariantCulture);
            }

            if (formatted is double db && Math.Abs(db - Math.Truncate(db)) < double.Epsilon)
            {
                return ((long)db).ToString(CultureInfo.InvariantCulture);
            }
        }

        return formatted;
    }

    public static string? ToText(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    public static object? ToNumeric(object? value)
    {
        if (value is null || value is bool)
        {
            if (value is bool b)
            {
                return b ? 1 : 0;
            }

            return null;
        }

        switch (value)
        {
            case int or long or short or byte:
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            case decimal d:
                return d;
            case double db:
                return Convert.ToDecimal(db, CultureInfo.InvariantCulture);
            case float f:
                return Convert.ToDecimal(f, CultureInfo.InvariantCulture);
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim().Replace(" ", "", StringComparison.Ordinal);
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        text = text.Replace(',', '.');
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
        {
            return whole;
        }

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        var digits = new string(text.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var extracted)
            ? extracted
            : null;
    }

    public static DateTime? ToDateTime(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case DateTime dt:
                return dt;
            case DateTimeOffset dto:
                return dto.UtcDateTime;
            case DateOnly dateOnly:
                return dateOnly.ToDateTime(TimeOnly.MinValue);
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var microsoft = MicrosoftJsonDate.Match(text);
        if (microsoft.Success
            && long.TryParse(microsoft.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        }

        var cleaned = text.Replace("Z", "", StringComparison.Ordinal);
        if (cleaned.Length > 26)
        {
            cleaned = cleaned[..26];
        }

        string[] formats =
        [
            "yyyy-MM-ddTHH:mm:ss.fffffff",
            "yyyy-MM-ddTHH:mm:ss.fff",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-dd HH:mm:ss.fffffff",
            "yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd",
            "dd/MM/yyyy HH:mm:ss",
            "dd/MM/yyyy",
            "MM/dd/yyyy HH:mm:ss",
            "MM/dd/yyyy"
        ];

        if (DateTime.TryParseExact(cleaned, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
        {
            return parsed;
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed)
            ? parsed
            : null;
    }

    public static bool AreEquivalent(object? left, object? right, IndexFieldType type)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        var normalizedLeft = FormatForDocuWare(left, type);
        var normalizedRight = FormatForDocuWare(right, type);
        if (normalizedLeft is null && normalizedRight is null)
        {
            return true;
        }

        if (normalizedLeft is null || normalizedRight is null)
        {
            return false;
        }

        if (type == IndexFieldType.Numeric)
        {
            try
            {
                return Convert.ToDecimal(normalizedLeft, CultureInfo.InvariantCulture)
                    == Convert.ToDecimal(normalizedRight, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        if (type == IndexFieldType.DateTime
            && normalizedLeft is DateTime ldt
            && normalizedRight is DateTime rdt)
        {
            return ldt == rdt;
        }

        return string.Equals(
            Convert.ToString(normalizedLeft, CultureInfo.InvariantCulture),
            Convert.ToString(normalizedRight, CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }
}
