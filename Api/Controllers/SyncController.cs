using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Domain.Enums;
using DocuWareSageConnector.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DocuWareSageConnector.Api.Controllers;

[ApiController]
[Route("api/sync")]
public sealed class SyncController : ControllerBase
{
    private readonly ISyncTrackingStore _tracking;
    private readonly ISyncWorkQueue _queue;
    private readonly ISyncStatusSnapshot _snapshot;
    private readonly SynchronizationOptions _options;

    public SyncController(
        ISyncTrackingStore tracking,
        ISyncWorkQueue queue,
        ISyncStatusSnapshot snapshot,
        IOptions<SynchronizationOptions> options)
    {
        _tracking = tracking;
        _queue = queue;
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

    [HttpPost("run")]
    public async Task<ActionResult> Run(CancellationToken cancellationToken)
    {
        await _queue.EnqueueAsync(new SyncCycleRequest(), cancellationToken).ConfigureAwait(false);
        return Accepted(new { message = "Synchronization cycle queued." });
    }

    [HttpPost("supplier/{number}")]
    public Task<ActionResult> SyncSupplier(string number, CancellationToken cancellationToken) =>
        EnqueueAsync(new SyncCycleRequest { EntityType = EntityType.Supplier, SageNumber = number }, cancellationToken);

    [HttpPost("account/{number}")]
    public Task<ActionResult> SyncAccount(string number, CancellationToken cancellationToken) =>
        EnqueueAsync(new SyncCycleRequest { EntityType = EntityType.ChartOfAccounts, SageNumber = number }, cancellationToken);

    [HttpPost("section/{code}")]
    public Task<ActionResult> SyncSection(string code, CancellationToken cancellationToken) =>
        EnqueueAsync(new SyncCycleRequest { EntityType = EntityType.AnalyticSection, SageNumber = code }, cancellationToken);

    [HttpPost("document/{documentId:int}")]
    public Task<ActionResult> SyncDocument(
        int documentId,
        [FromQuery] EntityType entityType = EntityType.Supplier,
        CancellationToken cancellationToken = default) =>
        EnqueueAsync(new SyncCycleRequest { EntityType = entityType, DocumentId = documentId }, cancellationToken);

    [HttpPost("retry/{syncId:guid}")]
    public async Task<ActionResult> Retry(Guid syncId, CancellationToken cancellationToken)
    {
        await _tracking.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var record = await _tracking.GetByIdAsync(syncId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        record.Status = SyncStatus.Pending;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await _tracking.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        await _queue.EnqueueAsync(
            new SyncCycleRequest
            {
                EntityType = record.EntityType,
                SageNumber = record.SageNumber,
                DocumentId = record.DocuWareDocumentId,
                TrackingId = record.Id,
                Force = true
            },
            cancellationToken).ConfigureAwait(false);
        return Accepted(record.ToDto());
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

    private async Task<ActionResult> EnqueueAsync(SyncCycleRequest request, CancellationToken cancellationToken)
    {
        await _queue.EnqueueAsync(request, cancellationToken).ConfigureAwait(false);
        return Accepted(new { message = "Synchronization request queued.", request });
    }
}
