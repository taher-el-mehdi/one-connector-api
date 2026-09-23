using MySqlConnector;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class MappingFieldSchema
{
    public static void Ensure(MySqlConnection connection)
    {
        if (!TableExists(connection, "mapping_field") || ColumnExists(connection, "id_mapping_table"))
        {
            return;
        }

        if (!ColumnExists(connection, "id_oc_mapping_table"))
        {
            throw new InvalidOperationException(
                "mapping_field has no id_mapping_table column.");
        }

        if (ConstraintExists(connection, "fk_oc_mapping_field_table"))
        {
            Execute(connection, "ALTER TABLE mapping_field DROP FOREIGN KEY fk_oc_mapping_field_table");
        }

        Execute(
            connection,
            "ALTER TABLE mapping_field CHANGE COLUMN id_oc_mapping_table id_mapping_table INT NOT NULL");

        if (!ConstraintExists(connection, "fk_mapping_field_table"))
        {
            Execute(
                connection,
                """
                ALTER TABLE mapping_field
                ADD CONSTRAINT fk_mapping_field_table
                FOREIGN KEY (id_mapping_table) REFERENCES mapping_table (id) ON DELETE CASCADE
                """);
        }
    }

    private static void Execute(MySqlConnection connection, string sql)
    {
        using var command = new MySqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    private static bool TableExists(MySqlConnection connection, string table)
    {
        using var command = new MySqlCommand(
            """
            SELECT COUNT(*)
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table
            """,
            connection);
        command.Parameters.AddWithValue("@table", table);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static bool ColumnExists(MySqlConnection connection, string name)
    {
        using var command = new MySqlCommand(
            """
            SELECT COUNT(*)
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'mapping_field'
              AND COLUMN_NAME = @name
            """,
            connection);
        command.Parameters.AddWithValue("@name", name);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static bool ConstraintExists(MySqlConnection connection, string name)
    {
        using var command = new MySqlCommand(
            """
            SELECT COUNT(*)
            FROM information_schema.TABLE_CONSTRAINTS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'mapping_field'
              AND CONSTRAINT_NAME = @name
            """,
            connection);
        command.Parameters.AddWithValue("@name", name);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }
}
