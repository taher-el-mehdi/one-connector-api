namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class SettingTable
{
    public const string Name = "setting";
    public const string DocuWare = "Docuware";
    public const string Sage = "Sage";
    public const string Synchronization = "synchronization";

    public static bool IsConnectorType(string? type) =>
        type is DocuWare or Sage;

    public static bool IsStoredType(string? type) =>
        type is DocuWare or Sage or Synchronization;
}
