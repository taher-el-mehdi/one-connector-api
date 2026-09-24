namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class SettingTemplates
{
    public static IReadOnlyList<(string Key, string? Value)> For(string type) => type switch
    {
        SettingTable.Sage =>
        [
            (SettingKeys.ServerName, string.Empty),
            (SettingKeys.DatabaseName, string.Empty),
            (SettingKeys.AuthentificationMode, "Windows"),
            (SettingKeys.UserName, string.Empty),
            (SettingKeys.PasswordProtected, null),
            (SettingKeys.TrustServerCertificate, "true"),
            (SettingKeys.CommandTimeoutSeconds, "30"),
            (SettingKeys.SuppliersOnly, "true"),
            (SettingKeys.ChartTypeZeroOnly, "true")
        ],
        SettingTable.DocuWare =>
        [
            (SettingKeys.PlatformUrl, string.Empty),
            (SettingKeys.Organization, string.Empty),
            (SettingKeys.AuthenticationMode, "UserPassword"),
            (SettingKeys.UserName, string.Empty),
            (SettingKeys.PasswordProtected, null),
            (SettingKeys.ClientId, string.Empty),
            (SettingKeys.ClientSecretProtected, null),
            (SettingKeys.Scope, "docuware.platform openid")
        ],
        _ => []
    };
}
