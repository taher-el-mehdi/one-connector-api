using DocuWareSageConnector.Application.DTOs;

namespace DocuWareSageConnector.Application.Interfaces;

public sealed class SynchronizationRecordWrite
{
    public int SynchronizationId { get; init; }

    public Guid RunId { get; init; }

    public required string EntityId { get; init; }

    public string? DocuWareId { get; init; }

    public string? Error { get; init; }
}

public interface ISynchronizationExecutionStore
{
    Task<SynchronizationRunPageDto?> ListRunsAsync(
        int synchronizationId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<SynchronizationRunDto?> GetRunAsync(
        int synchronizationId,
        Guid runId,
        CancellationToken cancellationToken);

    Task UpsertRecordAsync(SynchronizationRecordWrite record, CancellationToken cancellationToken);

    Task<IReadOnlySet<string>> ListInsertedEntityIdsAsync(int synchronizationId, CancellationToken cancellationToken);

    Task<SynchronizationSourceRecordPageDto?> ListRecordsAsync(
        int synchronizationId,
        int page,
        int pageSize,
        Guid? lastRunId,
        CancellationToken cancellationToken);
}
