using DocuWareSageConnector.Domain.Enums;

namespace DocuWareSageConnector.Domain.Entities;

public sealed class SyncTrackingRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required SyncDirection Direction { get; init; }

    public required EntityType EntityType { get; init; }

    public string? SageNumber { get; set; }

    public int? DocuWareDocumentId { get; set; }

    public SyncStatus Status { get; set; } = SyncStatus.Pending;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastAttemptAt { get; set; }

    public DateTimeOffset? LastSuccessAt { get; set; }

    public int RetryCount { get; set; }

    public string? LastError { get; set; }

    public string? Fingerprint { get; set; }
}
