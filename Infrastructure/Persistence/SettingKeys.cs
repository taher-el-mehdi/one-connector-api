namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class SettingKeys
{
    public const string PlatformUrl = "platform_url";
    public const string Organization = "organization";
    public const string AuthenticationMode = "authentication_mode";
    public const string UserName = "user_name";
    public const string PasswordProtected = "password_protected";
    public const string ClientId = "client_id";
    public const string ClientSecretProtected = "client_secret_protected";
    public const string Scope = "scope";

    public const string ServerName = "server_name";
    public const string DatabaseName = "database_name";
    public const string AuthentificationMode = "authentification_mode";
    public const string TrustServerCertificate = "trust_server_certificate";
    public const string CommandTimeoutSeconds = "command_timeout_seconds";
    public const string SuppliersOnly = "suppliers_only";
    public const string ChartTypeZeroOnly = "chart_type_zero_only";

    public const string Enabled = "enabled";
    public const string IntervalSeconds = "interval_seconds";
    public const string MaxRetries = "max_retries";
    public const string FirstRetryDelaySeconds = "first_retry_delay_seconds";
    public const string InsertMissingInSage = "insert_missing_in_sage";
    public const string ApplySageWrites = "apply_sage_writes";
    public const string SageToDocuWare = "sage_to_docuware";
    public const string DocuWareToSage = "docuware_to_sage";
}
