using DocuWareSageConnector.Domain.Enums;

namespace DocuWareSageConnector.Domain.Entities;

public sealed class IndexFieldValue
{
    public required string Name { get; init; }

    public IndexFieldType Type { get; init; }

    public object? Value { get; init; }
}
