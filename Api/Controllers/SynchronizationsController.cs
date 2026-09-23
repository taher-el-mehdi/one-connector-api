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
    private readonly ISyncWorkQueue _queue;

    public SynchronizationsController(ISynchronizationStore synchronizations, ISyncWorkQueue queue)
    {
        _synchronizations = synchronizations;
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
