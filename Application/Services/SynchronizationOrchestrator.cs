using System.Threading.Channels;
using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Domain.Enums;
using DocuWareSageConnector.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DocuWareSageConnector.Application.Services;

public sealed class SynchronizationOrchestrator : ISynchronizationOrchestrator
{
    private static readonly EntityType[] AllEntities =
    [
        EntityType.Supplier,
        EntityType.ChartOfAccounts,
        EntityType.AnalyticSection
    ];

    private readonly ISageToDocuWareSyncUseCase _sageToDocuWare;
    private readonly IDocuWareToSageSyncUseCase _docuWareToSage;
    private readonly SynchronizationOptions _options;
    private readonly ISyncStatusSnapshot _snapshot;
    private readonly ILogger<SynchronizationOrchestrator> _logger;

    public SynchronizationOrchestrator(
        ISageToDocuWareSyncUseCase sageToDocuWare,
        IDocuWareToSageSyncUseCase docuWareToSage,
        IOptions<SynchronizationOptions> options,
        ISyncStatusSnapshot snapshot,
        ILogger<SynchronizationOrchestrator> logger)
    {
        _sageToDocuWare = sageToDocuWare;
        _docuWareToSage = docuWareToSage;
        _options = options.Value;
        _snapshot = snapshot;
        _logger = logger;
    }

    public async Task<SyncCycleResult> RunCycleAsync(SyncCycleRequest request, CancellationToken cancellationToken)
    {
        var syncId = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow;
        var results = new List<EntitySyncResult>();
        var entities = request.EntityType is { } one ? new[] { one } : AllEntities;
        var sageToDocuWare = request.SageToDocuWare ?? _options.SageToDocuWare;
        var docuWareToSage = request.DocuWareToSage ?? _options.DocuWareToSage;

        _logger.LogInformation(
            "SyncId={SyncId} SynchronizationId={SynchronizationId} Status=Started",
            syncId,
            request.SynchronizationId);

        try
        {
            _snapshot.BeginCycle(syncId);
            foreach (var entityType in entities)
            {
                if (sageToDocuWare)
                {
                    results.Add(await RunDirectionAsync(
                        entityType,
                        SyncDirection.SageToDocuWare,
                        () => _sageToDocuWare.ExecuteAsync(entityType, request, syncId, cancellationToken),
                        syncId).ConfigureAwait(false));
                }

                if (docuWareToSage)
                {
                    results.Add(await RunDirectionAsync(
                        entityType,
                        SyncDirection.DocuWareToSage,
                        () => _docuWareToSage.ExecuteAsync(entityType, request, syncId, cancellationToken),
                        syncId).ConfigureAwait(false));
                }
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
