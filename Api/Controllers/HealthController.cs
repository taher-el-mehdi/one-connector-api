using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocuWareSageConnector.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    private readonly IDocuWareDocumentService _docuWare;
    private readonly ISageRepositoryFactory _sage;
    private readonly ISyncTrackingStore _tracking;

    public HealthController(
        IDocuWareDocumentService docuWare,
        ISageRepositoryFactory sage,
        ISyncTrackingStore tracking)
    {
        _docuWare = docuWare;
        _sage = sage;
        _tracking = tracking;
    }

    [HttpGet]
    public async Task<ActionResult<HealthResponse>> Get(CancellationToken cancellationToken)
    {
        var docuWare = await ProbeAsync(() => _docuWare.TestConnectionAsync(cancellationToken)).ConfigureAwait(false);
        var sage = await ProbeAsync(() => _sage.TestConnectionAsync(cancellationToken)).ConfigureAwait(false);
        var tracking = await ProbeAsync(() => _tracking.InitializeAsync(cancellationToken)).ConfigureAwait(false);

        var degraded = docuWare.Status != "Healthy" || sage.Status != "Healthy" || tracking.Status != "Healthy";
        var response = new HealthResponse
        {
            Status = degraded ? "Degraded" : "Healthy",
            Timestamp = DateTimeOffset.UtcNow,
            DocuWare = docuWare,
            Sage = sage,
            Tracking = tracking
        };

        return degraded ? StatusCode(StatusCodes.Status503ServiceUnavailable, response) : Ok(response);
    }

    [HttpGet("docuware")]
    public async Task<ActionResult<ComponentHealth>> DocuWare(CancellationToken cancellationToken)
    {
        var result = await ProbeAsync(() => _docuWare.TestConnectionAsync(cancellationToken)).ConfigureAwait(false);
        return result.Status == "Healthy" ? Ok(result) : StatusCode(StatusCodes.Status503ServiceUnavailable, result);
    }

    [HttpGet("sage")]
    public async Task<ActionResult<ComponentHealth>> Sage(CancellationToken cancellationToken)
    {
        var result = await ProbeAsync(() => _sage.TestConnectionAsync(cancellationToken)).ConfigureAwait(false);
        return result.Status == "Healthy" ? Ok(result) : StatusCode(StatusCodes.Status503ServiceUnavailable, result);
    }

    private static async Task<ComponentHealth> ProbeAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
            return new ComponentHealth { Status = "Healthy" };
        }
        catch (Exception ex)
        {
            return new ComponentHealth { Status = "Unhealthy", Detail = ex.Message };
        }
    }
}
