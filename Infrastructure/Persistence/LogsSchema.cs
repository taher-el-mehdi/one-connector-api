using MySqlConnector;

namespace DocuWareSageConnector.Infrastructure.Persistence;

/// <summary>
/// Reshapes an existing <c>logs</c> table to id_synchronization / error_message / file.
/// Mirrors <c>Database/alter_logs.sql</c> but only runs the steps that are still needed.
/// </summary>
internal static class LogsSchema
{
    public static void Ensure(MySqlConnection connection)
    {
        if (!TableExists(connection, "logs"))
        {
            return;
        }

        EnsureColumn(connection, "id_synchronization", "ALTER TABLE logs ADD COLUMN id_synchronization INT NULL");
        EnsureColumn(connection, "error_message", "ALTER TABLE logs ADD COLUMN error_message TEXT NULL AFTER retry_count");
        EnsureColumn(connection, "file", "ALTER TABLE logs ADD COLUMN `file` JSON NULL AFTER error_message");

        if (ColumnExists(connection, "direction")
            || ColumnExists(connection, "entity_type")
            || ColumnExists(connection, "sage_number")
            || ColumnExists(connection, "docuware_document_id")
            || ColumnExists(connection, "last_error")
            || ColumnExists(connection, "fingerprint"))
        {
            CopyLegacyIntoFile(connection);
        }

        if (IndexExists(connection, "uq_logs_lookup"))
        {
            Execute(connection, "ALTER TABLE logs DROP INDEX uq_logs_lookup");
        }

        DropColumnIfExists(connection, "direction");
        DropColumnIfExists(connection, "entity_type");
        DropColumnIfExists(connection, "sage_number");
        DropColumnIfExists(connection, "docuware_document_id");
        DropColumnIfExists(connection, "last_error");
        DropColumnIfExists(connection, "fingerprint");

        if (!IndexExists(connection, "ix_logs_synchronization"))
        {
            Execute(connection, "ALTER TABLE logs ADD KEY ix_logs_synchronization (id_synchronization)");
        }

        if (TableExists(connection, "synchronization") && !ConstraintExists(connection, "fk_logs_synchronization"))
        {
            Execute(
                connection,
                """
                ALTER TABLE logs
                ADD CONSTRAINT fk_logs_synchronization
                FOREIGN KEY (id_synchronization) REFERENCES synchronization (id)
                ON UPDATE CASCADE ON DELETE SET NULL
                """);
        }
    }

    private static void CopyLegacyIntoFile(MySqlConnection connection)
    {
        var hasLastError = ColumnExists(connection, "last_error");
        var hasDirection = ColumnExists(connection, "direction");
        if (!hasDirection && !hasLastError)
        {
            return;
        }

        if (hasDirection && hasLastError)
        {
            Execute(
                connection,
                """
                UPDATE logs
                SET
                  error_message = COALESCE(error_message, last_error),
                  file = COALESCE(
                    file,
                    JSON_OBJECT(
                      'direction', direction,
                      'entityType', entity_type,
                      'sageNumber', sage_number,
                      'docuWareDocumentId', docuware_document_id,
                      'fingerprint', fingerprint
                    ))
                """);
            return;
        }

        if (hasLastError)
        {
            Execute(
                connection,
                "UPDATE logs SET error_message = COALESCE(error_message, last_error) WHERE error_message IS NULL");
        }
    }

    private static void EnsureColumn(MySqlConnection connection, string column, string alterSql)
    {
        if (!ColumnExists(connection, column))
        {
            Execute(connection, alterSql);
        }
    }

    private static void DropColumnIfExists(MySqlConnection connection, string column)
    {
        if (ColumnExists(connection, column))
        {
            Execute(connection, $"ALTER TABLE logs DROP COLUMN `{column}`");
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
              AND TABLE_NAME = 'logs'
              AND COLUMN_NAME = @name
            """,
            connection);
        command.Parameters.AddWithValue("@name", name);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static bool IndexExists(MySqlConnection connection, string name)
    {
        using var command = new MySqlCommand(
            """
            SELECT COUNT(*)
            FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'logs'
              AND INDEX_NAME = @name
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
              AND TABLE_NAME = 'logs'
              AND CONSTRAINT_NAME = @name
            """,
            connection);
        command.Parameters.AddWithValue("@name", name);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }
}
