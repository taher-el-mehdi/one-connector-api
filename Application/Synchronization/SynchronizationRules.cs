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
        DateTimeOffset? nextRunAt)
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

        return new NormalizedSynchronization(
            normalizedDirection,
            RequireEndpoint(source, "Source"),
            RequireEndpoint(destination, "Destination"),
            mappingTableId.Value,
            normalizedCode,
            normalizedDescription,
            retries,
            timeout,
            nextRunAt?.UtcDateTime);
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

        var sageToDocuWare = string.Equals(source, "Sage", StringComparison.Ordinal)
            && string.Equals(destination, "DocuWare", StringComparison.Ordinal);
        var docuWareToSage = string.Equals(source, "DocuWare", StringComparison.Ordinal)
            && string.Equals(destination, "Sage", StringComparison.Ordinal);
        if (!sageToDocuWare && !docuWareToSage)
        {
            sageToDocuWare = string.Equals(source, "Sage", StringComparison.Ordinal);
            docuWareToSage = string.Equals(source, "DocuWare", StringComparison.Ordinal);
        }

        return (sageToDocuWare, docuWareToSage);
    }

    private static string RequireEndpoint(string? value, string label)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed is not ("Sage" or "DocuWare"))
        {
            throw new ArgumentException($"{label} must be Sage or DocuWare.");
        }

        return trimmed;
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
    DateTime? NextRunAt);

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
