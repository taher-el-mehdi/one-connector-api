using System.Globalization;
using DocuWareSageConnector.Infrastructure.Configuration;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class ConnectorConfigurationMap
{
    public static Dictionary<string, string?> From(
        DocuWareOptions docuWare,
        SageOptions sage,
        SynchronizationOptions synchronization,
        TrackingOptions tracking)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DocuWare:PlatformUrl"] = docuWare.PlatformUrl,
            ["DocuWare:Organization"] = docuWare.Organization,
            ["DocuWare:AuthenticationMode"] = docuWare.AuthenticationMode.ToString(),
            ["DocuWare:UserName"] = docuWare.UserName,
            ["DocuWare:Password"] = docuWare.Password,
            ["DocuWare:ClientId"] = docuWare.ClientId,
            ["DocuWare:ClientSecret"] = docuWare.ClientSecret,
            ["DocuWare:Scope"] = docuWare.Scope,
            ["DocuWare:Status"] = docuWare.Status ? "true" : "false",
            ["Sage:Server"] = sage.Server,
            ["Sage:Database"] = sage.Database,
            ["Sage:Authentication"] = sage.Authentication.ToString(),
            ["Sage:UserName"] = sage.UserName,
            ["Sage:Password"] = sage.Password,
            ["Sage:TrustServerCertificate"] = sage.TrustServerCertificate ? "true" : "false",
            ["Sage:CommandTimeoutSeconds"] = sage.CommandTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
            ["Sage:SuppliersOnly"] = sage.SuppliersOnly ? "true" : "false",
            ["Sage:ChartOfAccountsTypeZeroOnly"] = sage.ChartOfAccountsTypeZeroOnly ? "true" : "false",
            ["Sage:Status"] = sage.Status ? "true" : "false",
            ["Synchronization:Enabled"] = synchronization.Enabled ? "true" : "false",
            ["Synchronization:IntervalSeconds"] = synchronization.IntervalSeconds.ToString(CultureInfo.InvariantCulture),
            ["Synchronization:MaxRetries"] = synchronization.MaxRetries.ToString(CultureInfo.InvariantCulture),
            ["Synchronization:FirstRetryDelaySeconds"] = synchronization.FirstRetryDelaySeconds.ToString(CultureInfo.InvariantCulture),
            ["Synchronization:InsertMissingInSage"] = synchronization.InsertMissingInSage ? "true" : "false",
            ["Synchronization:ApplySageWrites"] = synchronization.ApplySageWrites ? "true" : "false",
            ["Synchronization:SageToDocuWare"] = synchronization.SageToDocuWare ? "true" : "false",
            ["Synchronization:DocuWareToSage"] = synchronization.DocuWareToSage ? "true" : "false",
            ["Synchronization:Status"] = synchronization.Status ? "true" : "false",
            ["Tracking:DatabasePath"] = tracking.DatabasePath
        };

        AddCabinet(data, "Supplier", docuWare.FileCabinets.Supplier);
        AddCabinet(data, "ChartOfAccounts", docuWare.FileCabinets.ChartOfAccounts);
        AddCabinet(data, "AnalyticSection", docuWare.FileCabinets.AnalyticSection);
        return data;
    }

    private static void AddCabinet(Dictionary<string, string?> data, string name, FileCabinetOptions cabinet)
    {
        data[$"DocuWare:FileCabinets:{name}:Name"] = cabinet.Name;
        data[$"DocuWare:FileCabinets:{name}:Id"] = cabinet.Id;
    }
}
