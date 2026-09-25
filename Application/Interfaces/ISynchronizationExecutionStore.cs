using DocuWareSageConnector.Application.DTOs;

namespace DocuWareSageConnector.Application.Interfaces;

public sealed class SynchronizationRecordWrite
{
    public int SynchronizationId { get; init; }

    public Guid RunId { get; init; }

    public required string SourceRecordId { get; init; }

    public string? SourceBusinessKey { get; init; }

    public string? SourceHash { get; init; }

    public string? DestinationRecordId { get; init; }

    public required string Status { get; init; }

    public string? ErrorMessage { get; init; }
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

    Task<IReadOnlySet<string>> ListInsertedSourceIdsAsync(int synchronizationId, CancellationToken cancellationToken);

    Task<SynchronizationSourceRecordPageDto?> ListRecordsAsync(
        int synchronizationId,
        int page,
        int pageSize,
        string? status,
        Guid? lastRunId,
        CancellationToken cancellationToken);
}
