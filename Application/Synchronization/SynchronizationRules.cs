namespace DocuWareSageConnector.Application.Synchronization;

public static class SynchronizationRules
{
    public const int CodeMaxLength = 64;
    public const int DescriptionMaxLength = 512;
    public const int MaxRetriesLimit = 100;
    public const int TimeoutSecondsLimit = 86_400;
    public const int DefaultMaxRetries = 3;
    public const int DefaultTimeoutSeconds = 300;

    public static NormalizedSynchronization Normalize(
        string? direction,
        string? source,
        string? destination,
        int? mappingTableId,
        string? code,
        string? description,
        int? maxRetries,
        int? timeoutSeconds,
        DateTimeOffset? nextRunAt,
        bool? recurrenceEnabled,
        string? recurrenceType,
        RecurrenceWeekdays recurrenceDays,
        string? recurrenceTime,
        int? intervalValue,
        string? intervalUnit,
        string? timezone,
        DateTime utcNow)
    {
        var normalizedDirection = direction?.Trim() ?? string.Empty;
        if (normalizedDirection is not ("one-way" or "two-ways"))
        {
            throw new ArgumentException("Direction must be one-way or two-ways.");
        }

        if (mappingTableId is null or <= 0)
        {
            throw new ArgumentException("Choose a mapping.");
        }

        var normalizedCode = code?.Trim() ?? string.Empty;
        if (normalizedCode.Length == 0)
        {
            throw new ArgumentException("Enter a code.");
        }

        if (normalizedCode.Length > CodeMaxLength)
        {
            throw new ArgumentException($"Code must be {CodeMaxLength} characters or fewer.");
        }

        var normalizedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (normalizedDescription is { Length: > DescriptionMaxLength })
        {
            throw new ArgumentException($"Description must be {DescriptionMaxLength} characters or fewer.");
        }

        var retries = maxRetries ?? DefaultMaxRetries;
        if (retries is < 0 or > MaxRetriesLimit)
        {
            throw new ArgumentException($"Max retries must be from 0 to {MaxRetriesLimit}.");
        }

        var timeout = timeoutSeconds ?? DefaultTimeoutSeconds;
        if (timeout is < 1 or > TimeoutSecondsLimit)
        {
            throw new ArgumentException($"Timeout must be from 1 to {TimeoutSecondsLimit} seconds.");
        }

        var normalizedSource = RequireEndpoint(source, "Source");
        var normalizedDestination = RequireEndpoint(destination, "Destination");
        if (SameConfigurationType(normalizedSource, normalizedDestination))
        {
            throw new ArgumentException("Source and destination must be different configuration types.");
        }

        var recurrence = RecurrenceSchedule.Normalize(
            recurrenceEnabled,
            recurrenceType,
            recurrenceDays,
            recurrenceTime,
            intervalValue,
            intervalUnit,
            timezone,
            utcNow);
        var scheduled = recurrence.Enabled ? recurrence.NextRunAt : nextRunAt?.UtcDateTime;

        return new NormalizedSynchronization(
            normalizedDirection,
            normalizedSource,
            normalizedDestination,
            mappingTableId.Value,
            normalizedCode,
            normalizedDescription,
            retries,
            timeout,
            scheduled,
            recurrence);
    }

    public static (bool SageToDocuWare, bool DocuWareToSage) RunDirections(
        string direction,
        string source,
        string destination)
    {
        if (string.Equals(direction, "two-ways", StringComparison.Ordinal))
        {
            return (true, true);
        }

        var sourceType = ConfigurationType(source);
        var destinationType = ConfigurationType(destination);
        var sageToDocuWare = sourceType == "Sage" && destinationType == "DocuWare";
        var docuWareToSage = sourceType == "DocuWare" && destinationType == "Sage";
        if (!sageToDocuWare && !docuWareToSage)
        {
            sageToDocuWare = sourceType == "Sage";
            docuWareToSage = sourceType == "DocuWare";
        }

        return (sageToDocuWare, docuWareToSage);
    }

    public static bool SameConfigurationType(string source, string destination) =>
        string.Equals(ConfigurationType(source), ConfigurationType(destination), StringComparison.Ordinal);

    public static (string Code, string Type) SplitEndpoint(string stored)
    {
        var separator = stored.LastIndexOf('|');
        if (separator <= 0 || separator == stored.Length - 1)
        {
            var type = ConfigurationType(stored);
            return (stored, type == "DocuWare" ? "Docuware" : type);
        }

        return (stored[..separator], stored[(separator + 1)..]);
    }

    private static string RequireEndpoint(string? value, string label)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new ArgumentException($"Choose a {label.ToLowerInvariant()} configuration.");
        }

        if (ConfigurationType(trimmed).Length == 0)
        {
            throw new ArgumentException($"{label} must be a Sage or DocuWare configuration.");
        }

        if (trimmed.Contains('|'))
        {
            var (code, type) = SplitEndpoint(trimmed);
            if (code.Length is 0 or > 64 || type.Length > 32)
            {
                throw new ArgumentException($"{label} must be a Sage or DocuWare configuration.");
            }

            var canonicalType = string.Equals(type, "Sage", StringComparison.OrdinalIgnoreCase) ? "Sage" : "Docuware";
            return $"{code}|{canonicalType}";
        }

        return trimmed;
    }

    private static string ConfigurationType(string value)
    {
        var type = value.Contains('|') ? value[(value.LastIndexOf('|') + 1)..] : value;
        if (type.Equals("Sage", StringComparison.OrdinalIgnoreCase))
        {
            return "Sage";
        }

        if (type.Equals("Docuware", StringComparison.OrdinalIgnoreCase) || type.Equals("DocuWare", StringComparison.OrdinalIgnoreCase))
        {
            return "DocuWare";
        }

        return string.Empty;
    }
}

public sealed record NormalizedSynchronization(
    string Direction,
    string Source,
    string Destination,
    int MappingTableId,
    string Code,
    string? Description,
    int MaxRetries,
    int TimeoutSeconds,
    DateTime? NextRunAt,
    NormalizedRecurrence Recurrence);

public static class SynchronizationFilterRules
{
    public const int FieldNameMaxLength = 128;
    public const int ValueMaxLength = 512;

    public static readonly string[] Operators =
    [
        "equals",
        "not_equals",
        "contains",
        "starts_with",
        "ends_with",
        "gt",
        "gte",
        "lt",
        "lte",
        "is_null",
        "not_null"
    ];

    public static NormalizedSynchronizationFilter Normalize(
        string? fieldName,
        string? operatorName,
        string? value,
        string? logicalOperator)
    {
        var field = fieldName?.Trim() ?? string.Empty;
        if (field.Length == 0)
        {
            throw new ArgumentException("Enter a field name.");
        }

        if (field.Length > FieldNameMaxLength)
        {
            throw new ArgumentException($"Field name must be {FieldNameMaxLength} characters or fewer.");
        }

        var op = operatorName?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!Operators.Contains(op, StringComparer.Ordinal))
        {
            throw new ArgumentException("Choose an operator.");
        }

        var logical = string.IsNullOrWhiteSpace(logicalOperator) ? "AND" : logicalOperator.Trim().ToUpperInvariant();
        if (logical is not ("AND" or "OR"))
        {
            throw new ArgumentException("Logical operator must be AND or OR.");
        }

        string? normalizedValue = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (op is "is_null" or "not_null")
        {
            normalizedValue = null;
        }
        else if (normalizedValue is null)
        {
            throw new ArgumentException("Enter a value.");
        }
        else if (normalizedValue.Length > ValueMaxLength)
        {
            throw new ArgumentException($"Value must be {ValueMaxLength} characters or fewer.");
        }

        return new NormalizedSynchronizationFilter(field, op, normalizedValue, logical);
    }
}

public sealed record NormalizedSynchronizationFilter(
    string FieldName,
    string Operator,
    string? Value,
    string LogicalOperator);
