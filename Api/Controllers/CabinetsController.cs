using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DocuWareSageConnector.Api.Controllers;

[ApiController]
[Route("api/cabinets")]
public sealed class CabinetsController : ControllerBase
{
    private readonly IDocuWareDocumentService _docuWare;
    private readonly IEntityMappingStore _mappings;

    public CabinetsController(IDocuWareDocumentService docuWare, IEntityMappingStore mappings)
    {
        _docuWare = docuWare;
        _mappings = mappings;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FileCabinetDto>>> Get(CancellationToken cancellationToken)
    {
        var cabinets = await _docuWare.ListFileCabinetsAsync(cancellationToken).ConfigureAwait(false);
        var labels = await _mappings.GetLabelsAsync(cancellationToken).ConfigureAwait(false);
        return Ok(cabinets.Select(cabinet => new FileCabinetDto
        {
            Id = cabinet.Id,
            Name = cabinet.Name,
            IsBasket = cabinet.IsBasket,
            UsedFor = labels.EntityFor(cabinet.Name)
        }).ToArray());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<FileCabinetDetailDto>> GetOne(string id, CancellationToken cancellationToken)
    {
        var cabinet = await _docuWare.GetFileCabinetAsync(id, cancellationToken).ConfigureAwait(false);
        if (cabinet is null)
        {
            return NotFound();
        }

        var labels = await _mappings.GetLabelsAsync(cancellationToken).ConfigureAwait(false);
        return Ok(new FileCabinetDetailDto
        {
            Id = cabinet.Id,
            Name = cabinet.Name,
            IsBasket = cabinet.IsBasket,
            UsedFor = labels.EntityFor(cabinet.Name),
            Fields = cabinet.Fields
        });
    }
}
