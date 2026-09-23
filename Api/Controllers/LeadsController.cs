using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Application.Leads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocuWareSageConnector.Api.Controllers;

[ApiController]
[Route("api/leads")]
public sealed class LeadsController : ControllerBase
{
    private readonly ILeadStore _leads;

    public LeadsController(ILeadStore leads)
    {
        _leads = leads;
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<LeadCreatedResponse>> Create(CreateLeadRequest request, CancellationToken cancellationToken)
    {
        var lead = LeadRules.Normalize(request);
        if (lead is null)
        {
            return BadRequest(new { code = "invalid_lead" });
        }

        var id = await _leads.AddAsync(lead, cancellationToken).ConfigureAwait(false);
        return StatusCode(StatusCodes.Status201Created, new LeadCreatedResponse { Id = id });
    }
}
