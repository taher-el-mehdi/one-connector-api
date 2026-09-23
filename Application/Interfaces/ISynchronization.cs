using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Domain.Enums;

namespace DocuWareSageConnector.Application.Interfaces;

public interface ISageToDocuWareSyncUseCase
{
    Task<EntitySyncResult> ExecuteAsync(
        EntityType entityType,
        SyncCycleRequest request,
        Guid syncId,
        CancellationToken cancellationToken);
}

public interface IDocuWareToSageSyncUseCase
{
    Task<EntitySyncResult> ExecuteAsync(
        EntityType entityType,
        SyncCycleRequest request,
        Guid syncId,
        CancellationToken cancellationToken);
}

public interface ISynchronizationOrchestrator
{
    Task<SyncCycleResult> RunCycleAsync(SyncCycleRequest request, CancellationToken cancellationToken);
}

public interface ISyncWorkQueue
{
    ValueTask EnqueueAsync(SyncCycleRequest request, CancellationToken cancellationToken);

    IAsyncEnumerable<SyncCycleRequest> ReadAllAsync(CancellationToken cancellationToken);
}

public interface ISyncStatusSnapshot
{
    DateTimeOffset? LastCycleAt { get; }

    Guid? LastSyncId { get; }

    bool CycleInProgress { get; }

    SyncCycleResult? LastCycle { get; }

    void BeginCycle(Guid syncId);

    void Record(SyncCycleResult result);

    void EndCycle();
}
