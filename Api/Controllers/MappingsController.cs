using System.Security.Claims;
using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DocuWareSageConnector.Api.Controllers;

[ApiController]
[Route("api/mappings")]
public sealed class MappingsController : ControllerBase
{
    private readonly IEntityMappingStore _mappings;

    public MappingsController(IEntityMappingStore mappings)
    {
        _mappings = mappings;
    }

    [HttpGet]
    public async Task<ActionResult<EntityMappingListDto>> Get(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _mappings.ListAsync(cancellationToken).ConfigureAwait(false));
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

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EntityMappingDto>> GetOne(int id, CancellationToken cancellationToken)
    {
        try
        {
            var mapping = await _mappings.GetAsync(id, cancellationToken).ConfigureAwait(false);
            return mapping is null ? NotFound() : Ok(mapping);
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

    [HttpPost]
    public async Task<ActionResult<EntityMappingDto>> Create(
        SaveEntityMappingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var created = await _mappings.CreateAsync(request, userId, cancellationToken).ConfigureAwait(false);
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
    public async Task<ActionResult<EntityMappingDto>> Update(
        int id,
        SaveEntityMappingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var updated = await _mappings.UpdateAsync(id, request, userId, cancellationToken).ConfigureAwait(false);
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

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await _mappings.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
            return deleted ? NoContent() : NotFound();
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

    [HttpPost("{id:int}/fields")]
    public async Task<ActionResult<EntityMappingFieldDto>> AddField(
        int id,
        SaveEntityMappingFieldRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var created = await _mappings.AddFieldAsync(id, request, userId, cancellationToken).ConfigureAwait(false);
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

    [HttpPut("{id:int}/fields/{fieldId:int}")]
    public async Task<ActionResult<EntityMappingFieldDto>> UpdateField(
        int id,
        int fieldId,
        SaveEntityMappingFieldRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var updated = await _mappings.UpdateFieldAsync(id, fieldId, request, userId, cancellationToken).ConfigureAwait(false);
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

    [HttpDelete("{id:int}/fields/{fieldId:int}")]
    public async Task<IActionResult> DeleteField(int id, int fieldId, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await _mappings.DeleteFieldAsync(id, fieldId, cancellationToken).ConfigureAwait(false);
            return deleted ? NoContent() : NotFound();
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

    private bool TryGetUserId(out Guid userId)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(id, out userId);
    }

    private ActionResult? ClientError(Exception exception) =>
        exception switch
        {
            ArgumentException argument => BadRequest(new { message = argument.Message }),
            MappingConflictException conflict => Conflict(new { message = conflict.Message }),
            MappingStoreException store => StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = store.Message }),
            _ => null
        };
}
