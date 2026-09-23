namespace DocuWareSageConnector.Domain.Entities;

public sealed class DocuWareDocumentInfo
{
    public required int Id { get; init; }

    public string? Title { get; init; }

    public IReadOnlyList<IndexFieldValue> Fields { get; init; } = [];
}
