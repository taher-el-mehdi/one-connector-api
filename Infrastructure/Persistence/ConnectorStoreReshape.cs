using System.Globalization;
using MySqlConnector;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class ConnectorStoreReshape
{
    public static void Apply(MySqlConnection connection)
    {
        ReplaceSyncHistory(connection);
        if (!TableExists(connection, "docuware_settings") && !TableExists(connection, "app_users"))
        {
            DropLegacy(connection);
            return;
        }

        if (TableExists(connection, "docuware_settings") && Count(connection, SettingTable.Name) == 0)
        {
            CopySettings(connection);
        }

        if (TableExists(connection, "app_users") && Count(connection, "user") == 0)
        {
            CopyUsers(connection);
        }

        DropLegacy(connection);
    }

    private static void ReplaceSyncHistory(MySqlConnection connection)
    {
        if (!TableExists(connection, "sync_history"))
        {
            return;
        }

        using (var drop = new MySqlCommand("DROP TABLE sync_history", connection))
        {
            drop.ExecuteNonQuery();
        }

        if (!TableExists(connection, "logs"))
        {
            return;
        }

        using var clear = new MySqlCommand("DELETE FROM logs", connection);
        clear.ExecuteNonQuery();
    }

    private static void CopySettings(MySqlConnection connection)
    {
        using var command = new MySqlCommand(
            """
            SELECT
                d.platform_url,
                d.organization_name,
                d.authentication_mode AS docuware_authentication_mode,
                d.user_name AS docuware_user_name,
                d.password_protected AS docuware_password_protected,
                d.client_id,
                d.client_secret_protected,
                d.scope,
                s.server_name,
                s.database_name,
                s.authentication_mode AS sage_authentication_mode,
                s.user_name AS sage_user_name,
                s.password_protected AS sage_password_protected,
                s.trust_server_certificate,
                s.command_timeout_seconds,
                s.suppliers_only,
                s.chart_type_zero_only,
                y.enabled,
                y.interval_seconds,
                y.max_retries,
                y.first_retry_delay_seconds,
                y.insert_missing_in_sage,
                y.apply_sage_writes,
                y.sage_to_docuware,
                y.docuware_to_sage
            FROM connector_profiles AS p
            INNER JOIN docuware_settings AS d ON d.profile_id = p.id
            INNER JOIN sage_settings AS s ON s.profile_id = p.id
            INNER JOIN synchronization_settings AS y ON y.profile_id = p.id
            WHERE p.is_active = 1
            LIMIT 1
            """,
            connection);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return;
        }

        var docuWare = new Dictionary<string, string?>
        {
            [SettingKeys.PlatformUrl] = reader.GetString("platform_url"),
            [SettingKeys.Organization] = reader.GetString("organization_name"),
            [SettingKeys.AuthenticationMode] = reader.GetString("docuware_authentication_mode"),
            [SettingKeys.UserName] = reader.GetString("docuware_user_name"),
            [SettingKeys.PasswordProtected] = NullableText(reader, "docuware_password_protected"),
            [SettingKeys.ClientId] = reader.GetString("client_id"),
            [SettingKeys.ClientSecretProtected] = NullableText(reader, "client_secret_protected"),
            [SettingKeys.Scope] = reader.GetString("scope")
        };
        var erp = new Dictionary<string, string?>
        {
            [SettingKeys.ServerName] = reader.GetString("server_name"),
            [SettingKeys.DatabaseName] = reader.GetString("database_name"),
            [SettingKeys.AuthentificationMode] = reader.GetString("sage_authentication_mode"),
            [SettingKeys.UserName] = reader.GetString("sage_user_name"),
            [SettingKeys.PasswordProtected] = NullableText(reader, "sage_password_protected"),
            [SettingKeys.TrustServerCertificate] = reader.GetBoolean("trust_server_certificate") ? "true" : "false",
            [SettingKeys.CommandTimeoutSeconds] = reader.GetInt32("command_timeout_seconds").ToString(CultureInfo.InvariantCulture),
            [SettingKeys.SuppliersOnly] = reader.GetBoolean("suppliers_only") ? "true" : "false",
            [SettingKeys.ChartTypeZeroOnly] = reader.GetBoolean("chart_type_zero_only") ? "true" : "false"
        };
        var sync = new Dictionary<string, string?>
        {
            [SettingKeys.Enabled] = reader.GetBoolean("enabled") ? "true" : "false",
            [SettingKeys.IntervalSeconds] = reader.GetInt32("interval_seconds").ToString(CultureInfo.InvariantCulture),
            [SettingKeys.MaxRetries] = reader.GetInt32("max_retries").ToString(CultureInfo.InvariantCulture),
            [SettingKeys.FirstRetryDelaySeconds] = reader.GetInt32("first_retry_delay_seconds").ToString(CultureInfo.InvariantCulture),
            [SettingKeys.InsertMissingInSage] = reader.GetBoolean("insert_missing_in_sage") ? "true" : "false",
            [SettingKeys.ApplySageWrites] = reader.GetBoolean("apply_sage_writes") ? "true" : "false",
            [SettingKeys.SageToDocuWare] = reader.GetBoolean("sage_to_docuware") ? "true" : "false",
            [SettingKeys.DocuWareToSage] = reader.GetBoolean("docuware_to_sage") ? "true" : "false"
        };
        reader.Close();

        using var transaction = connection.BeginTransaction();
        try
        {
            InsertAll(connection, transaction, SettingTable.DocuWare, "DocuWare", docuWare);
            InsertAll(connection, transaction, SettingTable.Sage, "Sage", erp);
            InsertAll(connection, transaction, SettingTable.Synchronization, "Synchronization", sync);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static void CopyUsers(MySqlConnection connection)
    {
        var email = ColumnExists(connection, "app_users", "email");
        var sql = email
            ? """
              INSERT INTO `user` (id, username, password_hash, display_name, email, is_active, created_at, updated_at, last_login_at)
              SELECT id, username, password_hash, display_name, email, is_active, created_at, updated_at, last_login_at
              FROM app_users
              """
            : """
              INSERT INTO `user` (id, username, password_hash, display_name, email, is_active, created_at, updated_at, last_login_at)
              SELECT id, username, password_hash, display_name, NULL, is_active, created_at, updated_at, last_login_at
              FROM app_users
              """;
        using var command = new MySqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    private static void DropLegacy(MySqlConnection connection)
    {
        using (var off = new MySqlCommand("SET FOREIGN_KEY_CHECKS = 0", connection))
        {
            off.ExecuteNonQuery();
        }

        foreach (var table in new[]
        {
            "user_sessions",
            "file_cabinets",
            "tracking_settings",
            "docuware_settings",
            "sage_settings",
            "synchronization_settings",
            "connector_profiles",
            "schema_migrations",
            "app_users"
        })
        {
            using var drop = new MySqlCommand($"DROP TABLE IF EXISTS `{table}`", connection);
            drop.ExecuteNonQuery();
        }

        using var on = new MySqlCommand("SET FOREIGN_KEY_CHECKS = 1", connection);
        on.ExecuteNonQuery();
    }

    private static void InsertAll(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string type,
        string description,
        Dictionary<string, string?> values)
    {
        foreach (var (key, value) in values)
        {
            using var command = new MySqlCommand(
                """
                INSERT INTO setting (type, code, description, `key`, `value`)
                VALUES (@type, @code, @description, @key, @value)
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue("@type", type);
            command.Parameters.AddWithValue("@code", type);
            command.Parameters.AddWithValue("@description", description);
            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@value", value is null ? DBNull.Value : value);
            command.ExecuteNonQuery();
        }
    }

    private static bool TableExists(MySqlConnection connection, string table)
    {
        using var command = new MySqlCommand(
            """
            SELECT 1 FROM information_schema.tables
            WHERE table_schema = DATABASE() AND table_name = @name
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue("@name", table);
        return command.ExecuteScalar() is not null and not DBNull;
    }

    private static bool ColumnExists(MySqlConnection connection, string table, string column)
    {
        using var command = new MySqlCommand(
            """
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name = @table AND column_name = @column
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        return command.ExecuteScalar() is not null and not DBNull;
    }

    private static int Count(MySqlConnection connection, string table)
    {
        using var command = new MySqlCommand($"SELECT COUNT(*) FROM `{table}`", connection);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static string? NullableText(MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
