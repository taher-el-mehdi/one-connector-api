using System.Collections.Concurrent;
using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Application.Synchronization;
using DocuWareSageConnector.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DocuWareSageConnector.Worker;

public sealed class SynchronizationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISyncWorkQueue _queue;
    private readonly ISynchronizationStore _synchronizations;
    private readonly SynchronizationOptions _options;
    private readonly ILogger<SynchronizationWorker> _logger;
    private readonly ConcurrentDictionary<int, byte> _running = new();

    public SynchronizationWorker(
        IServiceScopeFactory scopeFactory,
        ISyncWorkQueue queue,
        ISynchronizationStore synchronizations,
        IOptions<SynchronizationOptions> options,
        ILogger<SynchronizationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _synchronizations = synchronizations;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using (var initScope = _scopeFactory.CreateAsyncScope())
        {
            var tracking = initScope.ServiceProvider.GetRequiredService<ISyncTrackingStore>();
            await tracking.InitializeAsync(stoppingToken).ConfigureAwait(false);
        }

        if (!_options.Enabled)
        {
            _logger.LogInformation("Synchronization worker is disabled. Manual sync from a synchronization remains available.");
        }
        else
        {
            await RunDueSynchronizationsAsync(stoppingToken).ConfigureAwait(false);
        }

        var interval = TimeSpan.FromSeconds(Math.Max(_options.IntervalSeconds, 1));
        using var timer = new PeriodicTimer(interval);

        var waitTimer = WaitTimerAsync(timer, stoppingToken);
        var waitQueue = WaitQueueAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var completed = await Task.WhenAny(waitTimer, waitQueue).ConfigureAwait(false);
            SyncCycleRequest request = new();

            if (completed == waitQueue)
            {
                try
                {
                    request = await waitQueue.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                waitQueue = WaitQueueAsync(stoppingToken);
                if (request.SynchronizationId is int synchronizationId)
                {
                    await RunQueuedSynchronizationAsync(synchronizationId, stoppingToken).ConfigureAwait(false);
                    continue;
                }
            }
            else
            {
                try
                {
                    await waitTimer.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                waitTimer = WaitTimerAsync(timer, stoppingToken);
                if (!_options.Enabled)
                {
                    continue;
                }

                await RunDueSynchronizationsAsync(stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RunDueSynchronizationsAsync(CancellationToken stoppingToken)
    {
        IReadOnlyList<int> ids;
        try
        {
            ids = await _synchronizations.ListDueIdsAsync(DateTime.UtcNow, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not list synchronizations that are due.");
            return;
        }

        foreach (var id in ids)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            await RunQueuedSynchronizationAsync(id, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunQueuedSynchronizationAsync(int id, CancellationToken stoppingToken)
    {
        if (!_running.TryAdd(id, 0))
        {
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var ran = await RunClaimedOnceAsync(id, stoppingToken).ConfigureAwait(false);
                if (!ran)
                {
                    break;
                }
            }
        }
        finally
        {
            _running.TryRemove(id, out _);
        }
    }

    private async Task<bool> RunClaimedOnceAsync(int id, CancellationToken stoppingToken)
    {
        ClaimedSynchronization? claimed;
        try
        {
            claimed = await _synchronizations.ClaimAsync(id, DateTime.UtcNow, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "SynchronizationId={SynchronizationId} could not be claimed.", id);
            return false;
        }

        if (claimed is null)
        {
            return false;
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(claimed.TimeoutSeconds, 1));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeoutCts.CancelAfter(timeout);
        var (sageToDocuWare, docuWareToSage) = SynchronizationRules.RunDirections(
            claimed.Direction,
            claimed.Source,
            claimed.Destination);
        var success = false;
        var counts = new SynchronizationRunCounts();
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<ISynchronizationOrchestrator>();
            var result = await orchestrator.RunCycleAsync(
                new SyncCycleRequest
                {
                    SynchronizationId = id,
                    RunId = claimed.RunId,
                    Force = true,
                    SageToDocuWare = sageToDocuWare,
                    DocuWareToSage = docuWareToSage
                },
                timeoutCts.Token).ConfigureAwait(false);
            success = result.Failed == 0;
            counts = new SynchronizationRunCounts
            {
                TotalRecords = result.Created + result.Updated + result.Skipped + result.Failed,
                SuccessRecords = result.Created + result.Updated,
                FailedRecords = result.Failed,
                SkippedRecords = result.Skipped,
                ErrorMessage = result.Entities.Select(entity => entity.Error).FirstOrDefault(error => !string.IsNullOrWhiteSpace(error))
            };
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            counts = new SynchronizationRunCounts
            {
                FailedRecords = 1,
                ErrorMessage = $"Exceeded the timeout of {claimed.TimeoutSeconds} seconds."
            };
            _logger.LogError(
                "SynchronizationId={SynchronizationId} exceeded the timeout of {TimeoutSeconds} seconds.",
                id,
                claimed.TimeoutSeconds);
        }
        catch (Exception ex)
        {
            counts = new SynchronizationRunCounts { FailedRecords = 1, ErrorMessage = ex.Message };
            _logger.LogError(ex, "SynchronizationId={SynchronizationId} Status=Failed", id);
        }

        try
        {
            await _synchronizations.CompleteAsync(
                id,
                success,
                counts,
                TimeSpan.FromSeconds(Math.Max(_options.IntervalSeconds, 1)),
                TimeSpan.FromSeconds(Math.Max(_options.FirstRetryDelaySeconds, 1)),
                DateTime.UtcNow,
                stoppingToken).ConfigureAwait(false);
            _logger.LogInformation(
                "SynchronizationId={SynchronizationId} Status={Status}",
                id,
                success ? "success" : "failed");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "SynchronizationId={SynchronizationId} result could not be saved.", id);
            return false;
        }

        return true;
    }

    private static async Task WaitTimerAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private async Task<SyncCycleRequest> WaitQueueAsync(CancellationToken cancellationToken)
    {
        await foreach (var request in _queue.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            return request;
        }

        throw new OperationCanceledException(cancellationToken);
    }
}
