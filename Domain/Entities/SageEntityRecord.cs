namespace DocuWareSageConnector.Domain.Entities;

public sealed class SageEntityRecord
{
    public required string Key { get; init; }

    public int? CbMarq { get; init; }

    public IReadOnlyDictionary<string, object?> Columns { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
}
