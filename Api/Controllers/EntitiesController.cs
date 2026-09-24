using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace DocuWareSageConnector.Api.Controllers;

[ApiController]
[Route("api/suppliers")]
public sealed class SuppliersController : ControllerBase
{
    private readonly ISageRepositoryFactory _sage;

    public SuppliersController(ISageRepositoryFactory sage)
    {
        _sage = sage;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SupplierDto>>> Get(CancellationToken cancellationToken)
    {
        try
        {
            var rows = await _sage.Get(EntityType.Supplier).GetAllAsync(cancellationToken).ConfigureAwait(false);
            return Ok(rows.Select(row => new SupplierDto
            {
                Number = row.Key,
                Title = row.Columns.GetValueOrDefault("CT_Intitule")?.ToString(),
                Type = row.Columns.GetValueOrDefault("CT_Type"),
                City = row.Columns.GetValueOrDefault("CT_Ville")?.ToString(),
                Email = row.Columns.GetValueOrDefault("CT_EMail")?.ToString()
            }).ToArray());
        }
        catch (Exception ex)
        {
            return Unavailable(ex);
        }
    }

    private ObjectResult Unavailable(Exception exception) =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message });
}

[ApiController]
[Route("api/accounts")]
public sealed class AccountsController : ControllerBase
{
    private readonly ISageRepositoryFactory _sage;

    public AccountsController(ISageRepositoryFactory sage)
    {
        _sage = sage;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ChartOfAccountsDto>>> Get(CancellationToken cancellationToken)
    {
        try
        {
            var rows = await _sage.Get(EntityType.ChartOfAccounts).GetAllAsync(cancellationToken).ConfigureAwait(false);
            return Ok(rows.Select(row => new ChartOfAccountsDto
            {
                Number = row.Key,
                Title = row.Columns.GetValueOrDefault("CG_Intitule")?.ToString(),
                Nature = row.Columns.GetValueOrDefault("N_Nature")
            }).ToArray());
        }
        catch (Exception ex)
        {
            return Unavailable(ex);
        }
    }

    private ObjectResult Unavailable(Exception exception) =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message });
}

[ApiController]
[Route("api/sections")]
public sealed class AnalyticSectionsController : ControllerBase
{
    private readonly ISageRepositoryFactory _sage;

    public AnalyticSectionsController(ISageRepositoryFactory sage)
    {
        _sage = sage;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AnalyticSectionDto>>> Get(CancellationToken cancellationToken)
    {
        try
        {
            var rows = await _sage.Get(EntityType.AnalyticSection).GetAllAsync(cancellationToken).ConfigureAwait(false);
            return Ok(rows.Select(row => new AnalyticSectionDto
            {
                Code = row.Key,
                Description = row.Columns.GetValueOrDefault("CA_Intitule")?.ToString()
            }).ToArray());
        }
        catch (Exception ex)
        {
            return Unavailable(ex);
        }
    }

    private ObjectResult Unavailable(Exception exception) =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message });
}
