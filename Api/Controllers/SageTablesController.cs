using System.Text.RegularExpressions;
using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Sage.Sql;
using Microsoft.AspNetCore.Mvc;

namespace DocuWareSageConnector.Api.Controllers;

[ApiController]
[Route("api/entities/tables")]
public sealed partial class SageTablesController : ControllerBase
{
    private readonly SageSchemaReader _schema;
    private readonly IEntityMappingStore _mappings;

    public SageTablesController(SageSchemaReader schema, IEntityMappingStore mappings)
    {
        _schema = schema;
        _mappings = mappings;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SageTableDto>>> Get(CancellationToken cancellationToken)
    {
        try
        {
            var tables = await _schema.ListTablesAsync(cancellationToken).ConfigureAwait(false);
            var labels = await _mappings.GetLabelsAsync(cancellationToken).ConfigureAwait(false);
            return Ok(tables.Select(table => new SageTableDto
            {
                Schema = table.Schema,
                Name = table.Name,
                UsedFor = labels.CabinetFor(table.Name)
            }).ToArray());
        }
        catch (Exception ex)
        {
            return Unavailable(ex);
        }
    }

    [HttpGet("{schema}/{name}")]
    public async Task<ActionResult<SageTableDetailDto>> GetOne(string schema, string name, CancellationToken cancellationToken)
    {
        if (!IsIdentifier(schema) || !IsIdentifier(name))
        {
            return NotFound();
        }

        try
        {
            var table = await _schema.GetTableAsync(schema, name, cancellationToken).ConfigureAwait(false);
            if (table is null)
            {
                return NotFound();
            }

            var labels = await _mappings.GetLabelsAsync(cancellationToken).ConfigureAwait(false);
            return Ok(new SageTableDetailDto
            {
                Schema = table.Schema,
                Name = table.Name,
                UsedFor = labels.CabinetFor(table.Name),
                Fields = table.Fields
            });
        }
        catch (Exception ex)
        {
            return Unavailable(ex);
        }
    }

    private static bool IsIdentifier(string value) =>
        value.Length is > 0 and <= 128 && IdentifierPattern().IsMatch(value);

    private ObjectResult Unavailable(Exception exception) =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message });

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex IdentifierPattern();
}
