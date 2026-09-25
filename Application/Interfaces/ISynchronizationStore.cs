using DocuWareSageConnector.Application.DTOs;

namespace DocuWareSageConnector.Application.Interfaces;

public interface ISynchronizationStore
{
    Task<SynchronizationListDto> ListAsync(CancellationToken cancellationToken);

    Task<SynchronizationRecordDto?> GetAsync(int id, CancellationToken cancellationToken);

    Task<SynchronizationRecordDto> CreateAsync(SaveSynchronizationRequest request, Guid userId, CancellationToken cancellationToken);

    Task<SynchronizationRecordDto?> UpdateAsync(int id, SaveSynchronizationRequest request, Guid userId, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);

    Task<SynchronizationRecordDto?> QueueNowAsync(int id, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<int>> ListDueIdsAsync(DateTime utcNow, CancellationToken cancellationToken);

    Task<ClaimedSynchronization?> ClaimAsync(int id, DateTime utcNow, CancellationToken cancellationToken);

    Task CompleteAsync(
        int id,
        bool success,
        SynchronizationRunCounts counts,
        TimeSpan successInterval,
        TimeSpan retryDelay,
        DateTime utcNow,
        CancellationToken cancellationToken);

    Task<SynchronizationFilterListDto?> ListFiltersAsync(int synchronizationId, CancellationToken cancellationToken);

    Task<SynchronizationFilterDto?> CreateFilterAsync(
        int synchronizationId,
        SaveSynchronizationFilterRequest request,
        Guid userId,
        CancellationToken cancellationToken);

    Task<SynchronizationFilterDto?> UpdateFilterAsync(
        int synchronizationId,
        int filterId,
        SaveSynchronizationFilterRequest request,
        Guid userId,
        CancellationToken cancellationToken);

    Task<bool> DeleteFilterAsync(int synchronizationId, int filterId, CancellationToken cancellationToken);
}

public sealed class SynchronizationRunCounts
{
    public int TotalRecords { get; init; }

    public int SuccessRecords { get; init; }

    public int FailedRecords { get; init; }

    public int SkippedRecords { get; init; }

    public string? ErrorMessage { get; init; }
}

public sealed class ClaimedSynchronization
{
    public int Id { get; init; }

    public required string Direction { get; init; }

    public required string Source { get; init; }

    public required string Destination { get; init; }

    public int TimeoutSeconds { get; init; }

    public Guid RunId { get; init; }
}

public sealed class SynchronizationStoreException : Exception
{
    public SynchronizationStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
