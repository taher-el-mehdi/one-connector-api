using System.Globalization;
using DocuWareSageConnector.Domain.Enums;
using DocuWareSageConnector.Infrastructure.Configuration;
using MySqlConnector;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class ConnectorSettingsReader
{
    public static Dictionary<string, string?> Read(MySqlConnection connection, byte[] secretsKey)
    {
        var company = FindCompany(connection)
            ?? throw new InvalidOperationException("The connector store has no company.");
        var docuWareMap = ReadMap(connection, "setting_docuware", company);
        var erpMap = ReadMap(connection, "setting_erp", company);
        var syncMap = ReadMap(connection, "setting_synchronization", company);

        var docuWare = new DocuWareOptions
        {
            PlatformUrl = Text(docuWareMap, SettingKeys.PlatformUrl),
            Organization = Text(docuWareMap, SettingKeys.Organization),
            AuthenticationMode = ParseEnum<DocuWareAuthenticationMode>(
                Text(docuWareMap, SettingKeys.AuthenticationMode, DocuWareAuthenticationMode.UserPassword.ToString()),
                "DocuWare authentication"),
            UserName = Text(docuWareMap, SettingKeys.UserName),
            Password = SecretProtector.Unprotect(Optional(docuWareMap, SettingKeys.PasswordProtected), secretsKey),
            ClientId = Text(docuWareMap, SettingKeys.ClientId),
            ClientSecret = SecretProtector.Unprotect(Optional(docuWareMap, SettingKeys.ClientSecretProtected), secretsKey),
            Scope = Text(docuWareMap, SettingKeys.Scope, "docuware.platform openid"),
            Configured = Flag(connection, company, "setting_docuware", "configured"),
            Status = Flag(connection, company, "setting_docuware", "status")
        };
        var sage = new SageOptions
        {
            Server = Text(erpMap, SettingKeys.ServerName),
            Database = Text(erpMap, SettingKeys.DatabaseName),
            Authentication = ParseEnum<SageAuthenticationMode>(
                Text(erpMap, SettingKeys.AuthentificationMode, SageAuthenticationMode.Windows.ToString()),
                "ERP authentication"),
            UserName = Text(erpMap, SettingKeys.UserName),
            Password = SecretProtector.Unprotect(Optional(erpMap, SettingKeys.PasswordProtected), secretsKey),
            TrustServerCertificate = Bool(erpMap, SettingKeys.TrustServerCertificate, true),
            CommandTimeoutSeconds = Int(erpMap, SettingKeys.CommandTimeoutSeconds, 30),
            SuppliersOnly = Bool(erpMap, SettingKeys.SuppliersOnly, true),
            ChartOfAccountsTypeZeroOnly = Bool(erpMap, SettingKeys.ChartTypeZeroOnly, true),
            Configured = Flag(connection, company, "setting_erp", "configured"),
            Status = Flag(connection, company, "setting_erp", "status")
        };
        var synchronization = new SynchronizationOptions
        {
            Enabled = Bool(syncMap, SettingKeys.Enabled, true),
            IntervalSeconds = Int(syncMap, SettingKeys.IntervalSeconds, 30),
            MaxRetries = Int(syncMap, SettingKeys.MaxRetries, 3),
            FirstRetryDelaySeconds = Int(syncMap, SettingKeys.FirstRetryDelaySeconds, 2),
            InsertMissingInSage = Bool(syncMap, SettingKeys.InsertMissingInSage, false),
            ApplySageWrites = Bool(syncMap, SettingKeys.ApplySageWrites, true),
            SageToDocuWare = Bool(syncMap, SettingKeys.SageToDocuWare, true),
            DocuWareToSage = Bool(syncMap, SettingKeys.DocuWareToSage, true),
            Configured = Flag(connection, company, "setting_synchronization", "configured"),
            Status = Flag(connection, company, "setting_synchronization", "status")
        };

        return ConnectorConfigurationMap.From(
            docuWare,
            sage,
            synchronization,
            new TrackingOptions { DatabasePath = "logs" });
    }

    private static string? FindCompany(MySqlConnection connection)
    {
        using var command = new MySqlCommand(
            "SELECT name FROM company WHERE name = @name LIMIT 1",
            connection);
        command.Parameters.AddWithValue("@name", ConnectorCompany.Name);
        var named = command.ExecuteScalar();
        if (named is not null and not DBNull)
        {
            return Convert.ToString(named);
        }

        using var any = new MySqlCommand("SELECT name FROM company ORDER BY name LIMIT 1", connection);
        var value = any.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    private static Dictionary<string, string?> ReadMap(MySqlConnection connection, string table, string company)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        using var command = new MySqlCommand(
            $"SELECT `key`, `value` FROM {table} WHERE company = @company",
            connection);
        command.Parameters.AddWithValue("@company", company);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var ordinal = reader.GetOrdinal("value");
            map[reader.GetString("key")] = reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        return map;
    }

    private static bool Flag(MySqlConnection connection, string company, string table, string column)
    {
        using var command = new MySqlCommand(
            $"SELECT `{column}` FROM {table} WHERE company = @company LIMIT 1",
            connection);
        command.Parameters.AddWithValue("@company", company);
        var value = command.ExecuteScalar();
        if (value is null or DBNull)
        {
            return false;
        }

        return Convert.ToInt32(value) != 0;
    }

    private static string? Optional(Dictionary<string, string?> map, string key) =>
        map.TryGetValue(key, out var value) ? value : null;

    private static string Text(Dictionary<string, string?> map, string key, string fallback = "") =>
        Optional(map, key) ?? fallback;

    private static bool Bool(Dictionary<string, string?> map, string key, bool fallback)
    {
        var value = Optional(map, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return value == "1" ? true : value == "0" ? false : fallback;
    }

    private static int Int(Dictionary<string, string?> map, string key, int fallback) =>
        int.TryParse(Optional(map, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static TEnum ParseEnum<TEnum>(string value, string label) where TEnum : struct, Enum
    {
        if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
        {
            throw new InvalidOperationException($"The connector store has an unsupported {label} value '{value}'.");
        }

        return parsed;
    }
}
