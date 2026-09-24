using MySqlConnector;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class DocuWareSettingsSchema
{
    private const string UnassignedUser = "00000000-0000-0000-0000-000000000000";

    public static void Ensure(MySqlConnection connection)
    {
        EnsureTable(connection, SettingTable.Name);
    }

    private static void EnsureTable(MySqlConnection connection, string table)
    {
        AddColumn(connection, table, "created_by", "created_by CHAR(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'");
        AddColumn(connection, table, "created_at", "created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)");
        AddColumn(connection, table, "updated_at", "updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)");
        AddColumn(connection, table, "updated_by", "updated_by CHAR(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'");
        AddColumn(connection, table, "configured", "configured TINYINT(1) NOT NULL DEFAULT 0");
        AddColumn(connection, table, "status", "status TINYINT(1) NOT NULL DEFAULT 0");
        AddColumn(connection, table, "required", "`required` TINYINT(1) NOT NULL DEFAULT 1");
        BackfillActors(connection, table);
    }

    private static void AddColumn(MySqlConnection connection, string table, string name, string definition)
    {
        if (ColumnExists(connection, table, name))
        {
            return;
        }

        using var command = new MySqlCommand($"ALTER TABLE {table} ADD COLUMN {definition}", connection);
        command.ExecuteNonQuery();
    }

    private static bool ColumnExists(MySqlConnection connection, string table, string name)
    {
        using var command = new MySqlCommand(
            """
            SELECT COUNT(*)
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @table
              AND COLUMN_NAME = @name
            """,
            connection);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@name", name);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static void BackfillActors(MySqlConnection connection, string table)
    {
        using var users = new MySqlCommand("SELECT COUNT(*) FROM `user` WHERE is_active = 1", connection);
        if (Convert.ToInt32(users.ExecuteScalar()) == 0)
        {
            return;
        }

        using var command = new MySqlCommand(
            $"""
            UPDATE {table}
            SET created_by = (SELECT id FROM `user` WHERE is_active = 1 ORDER BY created_at LIMIT 1),
                updated_by = (SELECT id FROM `user` WHERE is_active = 1 ORDER BY created_at LIMIT 1)
            WHERE created_by = @unassigned OR updated_by = @unassigned
            """,
            connection);
        command.Parameters.AddWithValue("@unassigned", UnassignedUser);
        command.ExecuteNonQuery();
    }
}
