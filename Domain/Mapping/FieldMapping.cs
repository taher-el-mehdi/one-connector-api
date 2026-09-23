using DocuWareSageConnector.Domain.Enums;

namespace DocuWareSageConnector.Domain.Mapping;

public sealed class FieldMapping
{
    public required string DocuWareField { get; init; }

    public string? SageColumn { get; init; }

    public required IndexFieldType Type { get; init; }

    public bool IsKey { get; init; }

    public bool SkipSageWrite { get; init; }

    public bool IsComputed { get; init; }
}
