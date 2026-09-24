using System.Threading.Channels;
using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Application.UseCases;
using DocuWareSageConnector.Domain.Enums;

namespace DocuWareSageConnector.Application.Services;

public sealed class SynchronizationOrchestrator : ISynchronizationOrchestrator
{
    private readonly IMappedSageToDocuWareSync _mappedSageToDocuWare;
    private readonly ISyncStatusSnapshot _snapshot;
    private readonly ILogger<SynchronizationOrchestrator> _logger;

    public SynchronizationOrchestrator(
        IMappedSageToDocuWareSync mappedSageToDocuWare,
        ISyncStatusSnapshot snapshot,
        ILogger<SynchronizationOrchestrator> logger)
    {
        _mappedSageToDocuWare = mappedSageToDocuWare;
        _snapshot = snapshot;
        _logger = logger;
    }

    public async Task<SyncCycleResult> RunCycleAsync(SyncCycleRequest request, CancellationToken cancellationToken)
    {
        var syncId = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow;
        var results = new List<EntitySyncResult>();
        var sageToDocuWare = request.SageToDocuWare ?? true;

        _logger.LogInformation(
            "SyncId={SyncId} SynchronizationId={SynchronizationId} Status=Started",
            syncId,
            request.SynchronizationId);

        try
        {
            _snapshot.BeginCycle(syncId);
            if (request.SynchronizationId is int synchronizationId && sageToDocuWare)
            {
                results.Add(await RunDirectionAsync(
                    EntityType.Supplier,
                    SyncDirection.SageToDocuWare,
                    () => _mappedSageToDocuWare.ExecuteAsync(synchronizationId, syncId, request.Force, cancellationToken),
                    syncId).ConfigureAwait(false));
            }

            var completed = new SyncCycleResult
            {
                SyncId = syncId,
                StartedAt = started,
                CompletedAt = DateTimeOffset.UtcNow,
                Entities = results
            };
            _snapshot.Record(completed);
            _logger.LogInformation(
                "SyncId={SyncId} Status=Completed Created={Created} Updated={Updated} Skipped={Skipped} Failed={Failed}",
                syncId,
                completed.Created,
                completed.Updated,
                completed.Skipped,
                completed.Failed);
            return completed;
        }
        catch
        {
            _snapshot.EndCycle();
            throw;
        }
    }

    private async Task<EntitySyncResult> RunDirectionAsync(
        EntityType entityType,
        SyncDirection direction,
        Func<Task<EntitySyncResult>> execute,
        Guid syncId)
    {
        try
        {
            return await execute().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "SyncId={SyncId} Direction={Direction} Entity={Entity} Status=Failed",
                syncId,
                direction,
                entityType);
            return new EntitySyncResult
            {
                EntityType = entityType,
                Direction = direction,
                Failed = 1,
                Error = ex.Message
            };
        }
    }
}

public sealed class SyncWorkQueue : ISyncWorkQueue
{
    private readonly Channel<SyncCycleRequest> _channel = Channel.CreateUnbounded<SyncCycleRequest>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public ValueTask EnqueueAsync(SyncCycleRequest request, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(request, cancellationToken);

    public IAsyncEnumerable<SyncCycleRequest> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken) =>
        _channel.Reader.WaitToReadAsync(cancellationToken);
}

public sealed class SyncStatusSnapshot : ISyncStatusSnapshot
{
    private readonly object _gate = new();
    private DateTimeOffset? _lastCycleAt;
    private Guid? _lastSyncId;
    private bool _cycleInProgress;
    private SyncCycleResult? _lastCycle;

    public DateTimeOffset? LastCycleAt
    {
        get
        {
            lock (_gate)
            {
                return _lastCycleAt;
            }
        }
    }

    public Guid? LastSyncId
    {
        get
        {
            lock (_gate)
            {
                return _lastSyncId;
            }
        }
    }

    public bool CycleInProgress
    {
        get
        {
            lock (_gate)
            {
                return _cycleInProgress;
            }
        }
    }

    public SyncCycleResult? LastCycle
    {
        get
        {
            lock (_gate)
            {
                return _lastCycle;
            }
        }
    }

    public void BeginCycle(Guid syncId)
    {
        lock (_gate)
        {
            _cycleInProgress = true;
            _lastSyncId = syncId;
        }
    }

    public void Record(SyncCycleResult result)
    {
        lock (_gate)
        {
            _lastCycleAt = result.CompletedAt;
            _lastSyncId = result.SyncId;
            _lastCycle = result;
            _cycleInProgress = false;
        }
    }

    public void EndCycle()
    {
        lock (_gate)
        {
            _cycleInProgress = false;
        }
    }
}
