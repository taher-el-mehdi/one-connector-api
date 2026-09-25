using System.Security.Claims;
using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DocuWareSageConnector.Api.Controllers;

[ApiController]
[Route("api/synchronizations")]
public sealed class SynchronizationsController : ControllerBase
{
    private readonly ISynchronizationStore _synchronizations;
    private readonly ISynchronizationExecutionStore _execution;
    private readonly ISyncWorkQueue _queue;

    public SynchronizationsController(
        ISynchronizationStore synchronizations,
        ISynchronizationExecutionStore execution,
        ISyncWorkQueue queue)
    {
        _synchronizations = synchronizations;
        _execution = execution;
        _queue = queue;
    }

    [HttpGet]
    public async Task<ActionResult<SynchronizationListDto>> Get(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _synchronizations.ListAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (SynchronizationStoreException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SynchronizationRecordDto>> GetOne(int id, CancellationToken cancellationToken)
    {
        try
        {
            var record = await _synchronizations.GetAsync(id, cancellationToken).ConfigureAwait(false);
            return record is null ? NotFound() : Ok(record);
        }
        catch (SynchronizationStoreException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<ActionResult<SynchronizationRecordDto>> Create(
        SaveSynchronizationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var created = await _synchronizations.CreateAsync(request, userId, cancellationToken).ConfigureAwait(false);
            await WakeIfDueAsync(created, cancellationToken).ConfigureAwait(false);
            return CreatedAtAction(nameof(GetOne), new { id = created.Id }, created);
        }
        catch (Exception ex)
        {
            if (ClientError(ex) is { } error)
            {
                return error;
            }

            throw;
        }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<SynchronizationRecordDto>> Update(
        int id,
        SaveSynchronizationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var updated = await _synchronizations.UpdateAsync(id, request, userId, cancellationToken).ConfigureAwait(false);
            if (updated is null)
            {
                return NotFound();
            }

            await WakeIfDueAsync(updated, cancellationToken).ConfigureAwait(false);
            return Ok(updated);
        }
        catch (Exception ex)
        {
            if (ClientError(ex) is { } error)
            {
                return error;
            }

            throw;
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await _synchronizations.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
            return deleted ? NoContent() : NotFound();
        }
        catch (SynchronizationStoreException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
    }

    [HttpGet("{id:int}/filters")]
    public async Task<ActionResult<SynchronizationFilterListDto>> GetFilters(int id, CancellationToken cancellationToken)
    {
        try
        {
            var list = await _synchronizations.ListFiltersAsync(id, cancellationToken).ConfigureAwait(false);
            return list is null ? NotFound() : Ok(list);
        }
        catch (SynchronizationStoreException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/filters")]
    public async Task<ActionResult<SynchronizationFilterDto>> CreateFilter(
        int id,
        SaveSynchronizationFilterRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var created = await _synchronizations.CreateFilterAsync(id, request, userId, cancellationToken).ConfigureAwait(false);
            return created is null ? NotFound() : Ok(created);
        }
        catch (Exception ex)
        {
            if (ClientError(ex) is { } error)
            {
                return error;
            }

            throw;
        }
    }

    [HttpPut("{id:int}/filters/{filterId:int}")]
    public async Task<ActionResult<SynchronizationFilterDto>> UpdateFilter(
        int id,
        int filterId,
        SaveSynchronizationFilterRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var updated = await _synchronizations.UpdateFilterAsync(id, filterId, request, userId, cancellationToken).ConfigureAwait(false);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (Exception ex)
        {
            if (ClientError(ex) is { } error)
            {
                return error;
            }

            throw;
        }
    }

    [HttpDelete("{id:int}/filters/{filterId:int}")]
    public async Task<IActionResult> DeleteFilter(int id, int filterId, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await _synchronizations.DeleteFilterAsync(id, filterId, cancellationToken).ConfigureAwait(false);
            return deleted ? NoContent() : NotFound();
        }
        catch (SynchronizationStoreException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/run")]
    public async Task<ActionResult<SynchronizationRecordDto>> Run(int id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var queued = await _synchronizations.QueueNowAsync(id, userId, cancellationToken).ConfigureAwait(false);
            if (queued is null)
            {
                return NotFound();
            }

            await _queue.EnqueueAsync(
                new SyncCycleRequest { SynchronizationId = id, Force = true },
                cancellationToken).ConfigureAwait(false);
            return Accepted(queued);
        }
        catch (SynchronizationStoreException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
    }

    [HttpGet("{id:int}/runs")]
    public async Task<ActionResult<SynchronizationRunPageDto>> GetRuns(
        int id,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _execution.ListRunsAsync(id, page, pageSize, cancellationToken).ConfigureAwait(false);
            return result is null ? NotFound() : Ok(result);
        }
        catch (SynchronizationStoreException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
    }

    [HttpGet("{id:int}/runs/{runId:guid}")]
    public async Task<ActionResult<SynchronizationRunDto>> GetRun(
        int id,
        Guid runId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _execution.GetRunAsync(id, runId, cancellationToken).ConfigureAwait(false);
            return result is null ? NotFound() : Ok(result);
        }
        catch (SynchronizationStoreException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
    }

    [HttpGet("{id:int}/runs/{runId:guid}/records")]
    public async Task<ActionResult<SynchronizationSourceRecordPageDto>> GetRunRecords(
        int id,
        Guid runId,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _execution
                .ListRecordsAsync(id, page, pageSize, runId, cancellationToken)
                .ConfigureAwait(false);
            return result is null ? NotFound() : Ok(result);
        }
        catch (SynchronizationStoreException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
    }

    [HttpGet("{id:int}/records")]
    public async Task<ActionResult<SynchronizationSourceRecordPageDto>> GetRecords(
        int id,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _execution
                .ListRecordsAsync(id, page, pageSize, null, cancellationToken)
                .ConfigureAwait(false);
            return result is null ? NotFound() : Ok(result);
        }
        catch (SynchronizationStoreException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
    }

    private async Task WakeIfDueAsync(SynchronizationRecordDto record, CancellationToken cancellationToken)
    {
        if (record.NextRunAt is { } next && next <= DateTimeOffset.UtcNow.AddSeconds(1))
        {
            await _queue.EnqueueAsync(new SyncCycleRequest { SynchronizationId = record.Id }, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private bool TryGetUserId(out Guid userId)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(id, out userId);
    }

    private ActionResult? ClientError(Exception exception) =>
        exception switch
        {
            ArgumentException argument => BadRequest(new { message = argument.Message }),
            SynchronizationStoreException store => StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = store.Message }),
            _ => null
        };
}
