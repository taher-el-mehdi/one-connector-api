using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DocuWareSageConnector.Api.Controllers;

[ApiController]
[Route("api/sync")]
public sealed class SyncController : ControllerBase
{
    private readonly ISyncTrackingStore _tracking;
    private readonly ISyncStatusSnapshot _snapshot;
    private readonly SynchronizationOptions _options;

    public SyncController(
        ISyncTrackingStore tracking,
        ISyncStatusSnapshot snapshot,
        IOptions<SynchronizationOptions> options)
    {
        _tracking = tracking;
        _snapshot = snapshot;
        _options = options.Value;
    }

    [HttpGet("status")]
    public async Task<ActionResult<SyncStatusResponse>> Status(CancellationToken cancellationToken)
    {
        await _tracking.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var recent = await _tracking.ListAsync(null, 50, cancellationToken).ConfigureAwait(false);
        return Ok(new SyncStatusResponse
        {
            WorkerEnabled = _options.Enabled,
            IntervalSeconds = _options.IntervalSeconds,
            CycleInProgress = _snapshot.CycleInProgress,
            LastCycleAt = _snapshot.LastCycleAt,
            LastSyncId = _snapshot.LastSyncId,
            LastCycle = _snapshot.LastCycle is { } cycle ? ToSummary(cycle) : null,
            Recent = recent.Select(item => item.ToDto()).ToArray()
        });
    }

    [HttpGet("errors")]
    public async Task<ActionResult<IReadOnlyList<SyncTrackingRecordDto>>> Errors(CancellationToken cancellationToken)
    {
        await _tracking.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var errors = await _tracking.ListErrorsAsync(100, cancellationToken).ConfigureAwait(false);
        return Ok(errors.Select(item => item.ToDto()).ToArray());
    }

    private static SyncCycleSummaryDto ToSummary(SyncCycleResult result) =>
        new()
        {
            SyncId = result.SyncId,
            StartedAt = result.StartedAt,
            CompletedAt = result.CompletedAt,
            Created = result.Created,
            Updated = result.Updated,
            Skipped = result.Skipped,
            Failed = result.Failed,
            Entities = result.Entities.Select(item => new SyncCycleEntitySummaryDto
            {
                EntityType = item.EntityType.ToString(),
                Direction = item.Direction.ToString(),
                Created = item.Created,
                Updated = item.Updated,
                Skipped = item.Skipped,
                Failed = item.Failed,
                Error = item.Error
            }).ToArray()
        };
}
