using System.Globalization;
using DocuWareSageConnector.Domain.Enums;
using DocuWareSageConnector.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class ConnectorSettingsReader
{
    public static Dictionary<string, string?> Read(SqlConnection connection, byte[] secretsKey)
    {
        var (docuWare, sage, synchronization) = ReadOptions(connection, secretsKey);
        return ConnectorConfigurationMap.From(
            docuWare,
            sage,
            synchronization,
            new TrackingOptions { DatabasePath = "logs" });
    }

    public static void Apply(
        SqlConnection connection,
        byte[] secretsKey,
        DocuWareOptions docuWare,
        SageOptions sage,
        SynchronizationOptions synchronization)
    {
        var (nextDocuWare, nextSage, nextSynchronization) = ReadOptions(connection, secretsKey);
        docuWare.PlatformUrl = nextDocuWare.PlatformUrl;
        docuWare.Organization = nextDocuWare.Organization;
        docuWare.AuthenticationMode = nextDocuWare.AuthenticationMode;
        docuWare.UserName = nextDocuWare.UserName;
        docuWare.Password = nextDocuWare.Password;
        docuWare.ClientId = nextDocuWare.ClientId;
        docuWare.ClientSecret = nextDocuWare.ClientSecret;
        docuWare.Scope = nextDocuWare.Scope;
        docuWare.Configured = nextDocuWare.Configured;
        docuWare.Status = nextDocuWare.Status;
        sage.Server = nextSage.Server;
        sage.Database = nextSage.Database;
        sage.Authentication = nextSage.Authentication;
        sage.UserName = nextSage.UserName;
        sage.Password = nextSage.Password;
        sage.TrustServerCertificate = nextSage.TrustServerCertificate;
        sage.CommandTimeoutSeconds = nextSage.CommandTimeoutSeconds;
        sage.SuppliersOnly = nextSage.SuppliersOnly;
        sage.ChartOfAccountsTypeZeroOnly = nextSage.ChartOfAccountsTypeZeroOnly;
        sage.Configured = nextSage.Configured;
        sage.Status = nextSage.Status;
        synchronization.Enabled = nextSynchronization.Enabled;
        synchronization.IntervalSeconds = nextSynchronization.IntervalSeconds;
        synchronization.MaxRetries = nextSynchronization.MaxRetries;
        synchronization.FirstRetryDelaySeconds = nextSynchronization.FirstRetryDelaySeconds;
        synchronization.InsertMissingInSage = nextSynchronization.InsertMissingInSage;
        synchronization.ApplySageWrites = nextSynchronization.ApplySageWrites;
        synchronization.SageToDocuWare = nextSynchronization.SageToDocuWare;
        synchronization.DocuWareToSage = nextSynchronization.DocuWareToSage;
        synchronization.Configured = nextSynchronization.Configured;
        synchronization.Status = nextSynchronization.Status;
    }

    internal static SageOptions ReadSage(SqlConnection connection, byte[] secretsKey, string code)
    {
        var erpMap = ReadMap(connection, SettingTable.Sage, code);
        if (erpMap.Count == 0)
        {
            throw new InvalidOperationException($"Sage configuration '{code}' was not found.");
        }

        return new SageOptions
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
            ChartOfAccountsTypeZeroOnly = Bool(erpMap, SettingKeys.ChartTypeZeroOnly, true)
        };
    }

    private static (DocuWareOptions DocuWare, SageOptions Sage, SynchronizationOptions Synchronization) ReadOptions(
        SqlConnection connection,
        byte[] secretsKey)
    {
        var docuWareMap = ReadMap(connection, SettingTable.DocuWare, SettingTable.DocuWare);
        var erpMap = ReadMap(connection, SettingTable.Sage, SettingTable.Sage);
        var syncMap = ReadMap(connection, SettingTable.Synchronization, SettingTable.Synchronization);

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
            Configured = Flag(connection, SettingTable.DocuWare, "configured"),
            Status = Flag(connection, SettingTable.DocuWare, "status")
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
            Configured = Flag(connection, SettingTable.Sage, "configured"),
            Status = Flag(connection, SettingTable.Sage, "status")
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
            Configured = Flag(connection, SettingTable.Synchronization, "configured"),
            Status = Flag(connection, SettingTable.Synchronization, "status")
        };

        return (docuWare, sage, synchronization);
    }

    internal static Dictionary<string, string?> ReadMap(SqlConnection connection, string type, string code)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        using var command = new SqlCommand(
            """
            SELECT [key], [value] FROM setting
            WHERE type = @type AND code = @code
            """,
            connection);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@code", code);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var ordinal = reader.GetOrdinal("value");
            map[reader.GetString("key")] = reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        return map;
    }

    private static bool Flag(SqlConnection connection, string type, string column)
    {
        using var command = new SqlCommand(
            $"""
            SELECT [{column}] FROM setting
            WHERE type = @type AND code = @code
            """,
            connection);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@code", type);
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
