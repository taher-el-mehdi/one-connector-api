using System.Text.Json;
using System.Text.Json.Serialization;
using DocuWareSageConnector.Domain.Enums;

namespace DocuWareSageConnector.Domain.Entities;

/// <summary>
/// One <c>logs</c> row. Columns are id, id_synchronization, status, timestamps,
/// retry_count, error_message, and file. Direction, entity, Sage key, DocuWare id, and
/// fingerprint live inside <see cref="SerializeFile"/> JSON, not as table columns.
/// </summary>
public sealed class SyncTrackingRecord
{
    private static readonly JsonSerializerOptions FileJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Guid Id { get; init; } = Guid.NewGuid();

    public int? SynchronizationId { get; set; }

    public required SyncDirection Direction { get; set; }

    public required EntityType EntityType { get; set; }

    public string? SageNumber { get; set; }

    public int? DocuWareDocumentId { get; set; }

    public SyncStatus Status { get; set; } = SyncStatus.Pending;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastAttemptAt { get; set; }

    public DateTimeOffset? LastSuccessAt { get; set; }

    public int RetryCount { get; set; }

    public string? ErrorMessage { get; set; }

    public string? Fingerprint { get; set; }

    public void BindSynchronization(int? synchronizationId)
    {
        if (synchronizationId is int id)
        {
            SynchronizationId = id;
        }
    }

    public string? SerializeFile()
    {
        var payload = new TrackingFilePayload
        {
            Direction = (int)Direction,
            EntityType = (int)EntityType,
            SageNumber = SageNumber,
            DocuWareDocumentId = DocuWareDocumentId,
            Fingerprint = Fingerprint
        };
        return JsonSerializer.Serialize(payload, FileJsonOptions);
    }

    public static void ApplyFile(SyncTrackingRecord record, string? fileJson)
    {
        if (string.IsNullOrWhiteSpace(fileJson))
        {
            return;
        }

        var payload = JsonSerializer.Deserialize<TrackingFilePayload>(fileJson, FileJsonOptions);
        if (payload is null)
        {
            return;
        }

        if (payload.Direction is int direction)
        {
            record.Direction = (SyncDirection)direction;
        }

        if (payload.EntityType is int entityType)
        {
            record.EntityType = (EntityType)entityType;
        }

        record.SageNumber = payload.SageNumber;
        record.DocuWareDocumentId = payload.DocuWareDocumentId;
        record.Fingerprint = payload.Fingerprint;
    }

    private sealed class TrackingFilePayload
    {
        public int? Direction { get; set; }

        public int? EntityType { get; set; }

        public string? SageNumber { get; set; }

        public int? DocuWareDocumentId { get; set; }

        public string? Fingerprint { get; set; }
    }
}
