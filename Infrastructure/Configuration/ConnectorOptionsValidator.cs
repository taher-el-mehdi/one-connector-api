using Microsoft.Extensions.Options;

namespace DocuWareSageConnector.Infrastructure.Configuration;

public sealed class ConnectorOptionsValidator :
    IValidateOptions<DocuWareOptions>,
    IValidateOptions<SageOptions>,
    IValidateOptions<SynchronizationOptions>,
    IValidateOptions<TrackingOptions>
{
    private readonly IConfiguration _configuration;

    public ConnectorOptionsValidator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private bool SynchronizationEnabled =>
        _configuration.GetValue("Synchronization:Enabled", true);

    public ValidateOptionsResult Validate(string? name, DocuWareOptions options)
    {
        var errors = new List<string>();
        ValidateCabinet("Supplier", options.FileCabinets.Supplier, errors);
        ValidateCabinet("ChartOfAccounts", options.FileCabinets.ChartOfAccounts, errors);
        ValidateCabinet("AnalyticSection", options.FileCabinets.AnalyticSection, errors);

        if (!SynchronizationEnabled && string.IsNullOrWhiteSpace(options.PlatformUrl))
        {
            return errors.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(errors);
        }

        if (string.IsNullOrWhiteSpace(options.PlatformUrl))
        {
            errors.Add("DocuWare:PlatformUrl is required.");
        }
        else if (!Uri.TryCreate(options.PlatformUrl, UriKind.Absolute, out var uri)
                 || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("DocuWare:PlatformUrl must be an absolute http or https URL.");
        }

        if (options.AuthenticationMode == Domain.Enums.DocuWareAuthenticationMode.UserPassword)
        {
            if (string.IsNullOrWhiteSpace(options.UserName))
            {
                errors.Add("DocuWare:UserName is required for UserPassword authentication.");
            }
        }
        else if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            errors.Add("DocuWare:ClientId is required for AppRegistration authentication.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    public ValidateOptionsResult Validate(string? name, SageOptions options)
    {
        var errors = new List<string>();
        if (options.CommandTimeoutSeconds <= 0)
        {
            errors.Add("Sage:CommandTimeoutSeconds must be greater than 0.");
        }

        if (!SynchronizationEnabled && string.IsNullOrWhiteSpace(options.Server))
        {
            return errors.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(errors);
        }

        if (string.IsNullOrWhiteSpace(options.Server))
        {
            errors.Add("Sage:Server is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Database))
        {
            errors.Add("Sage:Database is required.");
        }

        if (options.Authentication == Domain.Enums.SageAuthenticationMode.Sql
            && string.IsNullOrWhiteSpace(options.UserName))
        {
            errors.Add("Sage:UserName is required when Sage:Authentication is Sql.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    public ValidateOptionsResult Validate(string? name, SynchronizationOptions options)
    {
        var errors = new List<string>();
        if (options.IntervalSeconds <= 0)
        {
            errors.Add("Synchronization:IntervalSeconds must be greater than 0.");
        }

        if (options.MaxRetries < 0)
        {
            errors.Add("Synchronization:MaxRetries cannot be negative.");
        }

        if (options.FirstRetryDelaySeconds < 0)
        {
            errors.Add("Synchronization:FirstRetryDelaySeconds cannot be negative.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    public ValidateOptionsResult Validate(string? name, TrackingOptions options)
    {
        return string.IsNullOrWhiteSpace(options.DatabasePath)
            ? ValidateOptionsResult.Fail("Tracking:DatabasePath is required.")
            : ValidateOptionsResult.Success;
    }

    private static void ValidateCabinet(string name, FileCabinetOptions cabinet, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(cabinet.Name) && string.IsNullOrWhiteSpace(cabinet.Id))
        {
            errors.Add($"DocuWare:FileCabinets:{name} must have Name and/or Id.");
        }
    }
}
