using System.Security.Claims;
using DocuWareSageConnector.Application;
using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DocuWareSageConnector.Api.Controllers;

[ApiController]
[Route("api/configuration")]
public sealed class ConfigurationController : ControllerBase
{
    private readonly DocuWareOptions _docuWare;
    private readonly SageOptions _sage;
    private readonly SynchronizationOptions _synchronization;
    private readonly TrackingOptions _tracking;
    private readonly IDocuWareSettingsStore _docuWareSettings;
    private readonly ISageSettingsStore _sageSettings;
    private readonly ISynchronizationSettingsStore _synchronizationSettings;
    private readonly IConfigurationCatalog _catalog;
    private readonly IConnectorIdentity _identity;

    public ConfigurationController(
        IOptions<DocuWareOptions> docuWare,
        IOptions<SageOptions> sage,
        IOptions<SynchronizationOptions> synchronization,
        IOptions<TrackingOptions> tracking,
        IDocuWareSettingsStore docuWareSettings,
        ISageSettingsStore sageSettings,
        ISynchronizationSettingsStore synchronizationSettings,
        IConfigurationCatalog catalog,
        IConnectorIdentity identity)
    {
        _docuWare = docuWare.Value;
        _sage = sage.Value;
        _synchronization = synchronization.Value;
        _tracking = tracking.Value;
        _docuWareSettings = docuWareSettings;
        _sageSettings = sageSettings;
        _synchronizationSettings = synchronizationSettings;
        _catalog = catalog;
        _identity = identity;
    }

    [HttpGet]
    public async Task<ActionResult<PublicConfigurationResponse>> Get(CancellationToken cancellationToken) =>
        Ok(new PublicConfigurationResponse
        {
            DocuWare = MapDocuWare(),
            Sage = MapSage(),
            Synchronization = MapSynchronization(),
            Tracking = new PublicTrackingConfiguration
            {
                DatabasePath = _tracking.DatabasePath
            },
            Entries = await _catalog.ListAsync(cancellationToken).ConfigureAwait(false)
        });

    [HttpGet("rows")]
    public async Task<ActionResult<IReadOnlyList<ConfigurationSettingRow>>> Rows(CancellationToken cancellationToken) =>
        Ok(await _catalog.ListRowsAsync(cancellationToken).ConfigureAwait(false));

    [HttpPost("import")]
    public async Task<ActionResult<ImportConfigurationResult>> Import(
        List<ConfigurationSettingRow> rows,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var result = await _catalog.ImportAsync(rows, userId, cancellationToken).ConfigureAwait(false);
            return Ok(result);
        }
        catch (SettingsValidationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost]
    public async Task<ActionResult<ConfigurationEntry>> Create(
        CreateConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var created = await _catalog.CreateAsync(request, userId, cancellationToken).ConfigureAwait(false);
            return Ok(created);
        }
        catch (SettingsValidationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPut("docuware")]
    public async Task<ActionResult<PublicDocuWareConfiguration>> UpdateDocuWare(
        UpdateDocuWareConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            await _docuWareSettings.UpdateAsync(request, userId, cancellationToken).ConfigureAwait(false);
            return Ok(MapDocuWare());
        }
        catch (SettingsValidationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("docuware/status")]
    public async Task<ActionResult<PublicDocuWareConfiguration>> SetDocuWareStatus(
        SetDocuWareStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        await _docuWareSettings.SetStatusAsync(request.Status, userId, cancellationToken).ConfigureAwait(false);
        return Ok(MapDocuWare());
    }

    [HttpPost("docuware/reveal")]
    public async Task<ActionResult<RevealDocuWareSecretResponse>> RevealDocuWareSecret(
        RevealDocuWareSecretRequest request,
        CancellationToken cancellationToken)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(id, out var userId))
        {
            return Unauthorized();
        }

        if (request.Field is not ("password" or "clientSecret"))
        {
            return BadRequest(new { message = "unknown_secret" });
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            return BadRequest(new { message = "password_required" });
        }

        var username = User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        var accepted = await _identity.VerifyPasswordAsync(userId, username, request.Password, cancellationToken).ConfigureAwait(false);
        if (!accepted)
        {
            return BadRequest(new { message = "password_rejected" });
        }

        var value = request.Field == "password" ? _docuWare.Password : _docuWare.ClientSecret;
        return Ok(new RevealDocuWareSecretResponse { Value = value });
    }

    [HttpPut("sage")]
    public async Task<ActionResult<PublicSageConfiguration>> UpdateSage(
        UpdateSageConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            await _sageSettings.UpdateAsync(request, userId, cancellationToken).ConfigureAwait(false);
            return Ok(MapSage());
        }
        catch (SettingsValidationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("sage/status")]
    public async Task<ActionResult<PublicSageConfiguration>> SetSageStatus(
        SetSageStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        await _sageSettings.SetStatusAsync(request.Status, userId, cancellationToken).ConfigureAwait(false);
        return Ok(MapSage());
    }

    [HttpPost("sage/reveal")]
    public async Task<ActionResult<RevealDocuWareSecretResponse>> RevealSagePassword(
        RevealSagePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(id, out var userId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            return BadRequest(new { message = "password_required" });
        }

        var username = User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        var accepted = await _identity.VerifyPasswordAsync(userId, username, request.Password, cancellationToken).ConfigureAwait(false);
        if (!accepted)
        {
            return BadRequest(new { message = "password_rejected" });
        }

        return Ok(new RevealDocuWareSecretResponse { Value = _sage.Password });
    }

    [HttpPut("synchronization")]
    public async Task<ActionResult<PublicSynchronizationConfiguration>> UpdateSynchronization(
        UpdateSynchronizationConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            await _synchronizationSettings.UpdateAsync(request, userId, cancellationToken).ConfigureAwait(false);
            return Ok(MapSynchronization());
        }
        catch (SettingsValidationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    private bool TryGetUserId(out Guid userId)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(id, out userId);
    }

    private PublicDocuWareConfiguration MapDocuWare()
    {
        return new PublicDocuWareConfiguration
        {
            PlatformUrl = _docuWare.PlatformUrl,
            Organization = _docuWare.Organization,
            AuthenticationMode = _docuWare.AuthenticationMode.ToString(),
            UserName = _docuWare.UserName,
            PasswordConfigured = !string.IsNullOrEmpty(_docuWare.Password),
            ClientId = _docuWare.ClientId,
            ClientSecretConfigured = !string.IsNullOrEmpty(_docuWare.ClientSecret),
            Scope = _docuWare.Scope,
            Status = _docuWare.Status,
            Supplier = Cabinet(_docuWare.FileCabinets.Supplier),
            ChartOfAccounts = Cabinet(_docuWare.FileCabinets.ChartOfAccounts),
            AnalyticSection = Cabinet(_docuWare.FileCabinets.AnalyticSection)
        };
    }

    private PublicSageConfiguration MapSage()
    {
        return new PublicSageConfiguration
        {
            Server = _sage.Server,
            Database = _sage.Database,
            Authentication = _sage.Authentication.ToString(),
            UserName = _sage.UserName,
            PasswordConfigured = !string.IsNullOrEmpty(_sage.Password),
            TrustServerCertificate = _sage.TrustServerCertificate,
            CommandTimeoutSeconds = _sage.CommandTimeoutSeconds,
            SuppliersOnly = _sage.SuppliersOnly,
            ChartOfAccountsTypeZeroOnly = _sage.ChartOfAccountsTypeZeroOnly,
            Status = _sage.Status
        };
    }

    private PublicSynchronizationConfiguration MapSynchronization()
    {
        return new PublicSynchronizationConfiguration
        {
            Enabled = _synchronization.Enabled,
            IntervalSeconds = _synchronization.IntervalSeconds,
            MaxRetries = _synchronization.MaxRetries,
            FirstRetryDelaySeconds = _synchronization.FirstRetryDelaySeconds,
            InsertMissingInSage = _synchronization.InsertMissingInSage,
            ApplySageWrites = _synchronization.ApplySageWrites,
            SageToDocuWare = _synchronization.SageToDocuWare,
            DocuWareToSage = _synchronization.DocuWareToSage,
            Status = _synchronization.Status
        };
    }

    private static PublicFileCabinetConfiguration Cabinet(FileCabinetOptions cabinet) =>
        new()
        {
            Name = cabinet.Name,
            Id = cabinet.Id
        };
}
