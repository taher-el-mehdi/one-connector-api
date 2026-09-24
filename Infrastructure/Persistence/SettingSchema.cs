using MySqlConnector;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class SettingSchema
{
    public static void MigrateLegacy(MySqlConnection connection)
    {
        Copy(connection, "setting_docuware", SettingTable.DocuWare);
        Copy(connection, "setting_erp", SettingTable.Sage);
        Copy(connection, "setting_synchronization", SettingTable.Synchronization);
        Drop(connection, "setting_docuware");
        Drop(connection, "setting_erp");
        Drop(connection, "setting_synchronization");
    }

    private static void Copy(MySqlConnection connection, string source, string type)
    {
        if (!TableExists(connection, source) || !TableExists(connection, SettingTable.Name))
        {
            return;
        }

        using var command = new MySqlCommand(
            $"""
            INSERT INTO setting
                (type, code, description, `key`, `value`, created_by, created_at, updated_at, updated_by, configured, status, `required`)
            SELECT @type, @code, @description, `key`, `value`, created_by, created_at, updated_at, updated_by, configured, status, `required`
            FROM `{source}` AS source
            WHERE NOT EXISTS (
                SELECT 1 FROM setting AS target
                WHERE target.type = @type
                  AND target.code = @code
                  AND target.`key` = source.`key`
            )
            """,
            connection);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@code", type);
        command.Parameters.AddWithValue("@description", Description(type));
        command.ExecuteNonQuery();
    }

    private static string Description(string type) => type switch
    {
        SettingTable.DocuWare => "DocuWare",
        SettingTable.Sage => "Sage",
        _ => "Synchronization"
    };

    private static void Drop(MySqlConnection connection, string table)
    {
        if (!TableExists(connection, table))
        {
            return;
        }

        using var command = new MySqlCommand($"DROP TABLE `{table}`", connection);
        command.ExecuteNonQuery();
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
}
