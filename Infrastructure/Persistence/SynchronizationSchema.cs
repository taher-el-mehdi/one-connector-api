using MySqlConnector;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class SynchronizationSchema
{
    public static void Ensure(MySqlConnection connection)
    {
        if (!TableExists(connection, "synchronization"))
        {
            return;
        }

        EnsureMappingColumn(connection);
        EnsureQueueColumns(connection);
    }

    private static void EnsureMappingColumn(MySqlConnection connection)
    {
        if (ColumnExists(connection, "id_mapping_table"))
        {
            return;
        }

        if (Count(connection, "SELECT COUNT(*) FROM synchronization") == 0)
        {
            Execute(connection, "ALTER TABLE synchronization ADD COLUMN id_mapping_table INT NOT NULL");
        }
        else if (RowsMissingUniqueMapping(connection))
        {
            throw new InvalidOperationException(
                "The synchronization table already has rows and mapping_table does not contain exactly one mapping. Leave one mapping, or remove the synchronization rows, then start the API again.");
        }
        else
        {
            Execute(connection, "ALTER TABLE synchronization ADD COLUMN id_mapping_table INT NULL");
            Execute(
                connection,
                """
                UPDATE synchronization AS sync
                INNER JOIN (
                    SELECT MIN(id) AS mapping_id
                    FROM mapping_table
                    HAVING COUNT(*) = 1
                ) AS only_mapping
                SET sync.id_mapping_table = only_mapping.mapping_id
                """);
            if (Count(connection, "SELECT COUNT(*) FROM synchronization WHERE id_mapping_table IS NULL") > 0)
            {
                throw new InvalidOperationException(
                    "The synchronization table already has rows and cannot gain id_mapping_table. Remove those rows, then start the API again.");
            }

            Execute(connection, "ALTER TABLE synchronization MODIFY COLUMN id_mapping_table INT NOT NULL");
        }

        if (ConstraintExists(connection, "fk_synchronization_mapping"))
        {
            return;
        }

        Execute(
            connection,
            """
            ALTER TABLE synchronization
            ADD CONSTRAINT fk_synchronization_mapping
            FOREIGN KEY (id_mapping_table) REFERENCES mapping_table (id) ON DELETE RESTRICT
            """);
    }

    private static void EnsureQueueColumns(MySqlConnection connection)
    {
        if (!ColumnExists(connection, "code"))
        {
            Execute(connection, "ALTER TABLE synchronization ADD COLUMN code VARCHAR(64) NULL");
        }

        Execute(
            connection,
            "UPDATE synchronization SET code = CONCAT('SYNC-', id) WHERE code IS NULL OR code = ''");
        if (ColumnNullable(connection, "code"))
        {
            Execute(connection, "ALTER TABLE synchronization MODIFY COLUMN code VARCHAR(64) NOT NULL");
        }

        if (!ColumnExists(connection, "description"))
        {
            Execute(connection, "ALTER TABLE synchronization ADD COLUMN description VARCHAR(512) NULL");
        }

        if (!ColumnExists(connection, "status"))
        {
            Execute(connection, "ALTER TABLE synchronization ADD COLUMN status VARCHAR(16) NULL");
        }

        if (!ColumnExists(connection, "max_retries"))
        {
            Execute(connection, "ALTER TABLE synchronization ADD COLUMN max_retries INT NOT NULL DEFAULT 3");
        }

        if (!ColumnExists(connection, "timeout_seconds"))
        {
            Execute(connection, "ALTER TABLE synchronization ADD COLUMN timeout_seconds INT NOT NULL DEFAULT 300");
        }

        if (!ColumnExists(connection, "next_run_at"))
        {
            Execute(connection, "ALTER TABLE synchronization ADD COLUMN next_run_at DATETIME(6) NULL");
        }

        if (!ColumnExists(connection, "retry_count"))
        {
            Execute(connection, "ALTER TABLE synchronization ADD COLUMN retry_count INT NOT NULL DEFAULT 0");
        }

        if (IndexExists(connection, "uq_synchronization_company_code"))
        {
            Execute(connection, "ALTER TABLE synchronization DROP INDEX uq_synchronization_company_code");
        }

        if (!IndexExists(connection, "uq_synchronization_code"))
        {
            Execute(connection, "ALTER TABLE synchronization ADD UNIQUE KEY uq_synchronization_code (code)");
        }

        if (!IndexExists(connection, "ix_synchronization_due"))
        {
            Execute(connection, "ALTER TABLE synchronization ADD KEY ix_synchronization_due (next_run_at)");
        }

        if (!ConstraintExists(connection, "chk_synchronization_status"))
        {
            Execute(
                connection,
                """
                ALTER TABLE synchronization
                ADD CONSTRAINT chk_synchronization_status
                CHECK (status IS NULL OR status IN ('success', 'failed'))
                """);
        }
    }

    private static bool RowsMissingUniqueMapping(MySqlConnection connection)
    {
        if (!TableExists(connection, "mapping_table"))
        {
            return true;
        }

        return Count(
            connection,
            """
            SELECT CASE WHEN (SELECT COUNT(*) FROM mapping_table) = 1 THEN 0 ELSE 1 END
            """) > 0;
    }

    private static int Count(MySqlConnection connection, string sql)
    {
        using var command = new MySqlCommand(sql, connection);
        return Convert.ToInt32(command.ExecuteScalar());
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
              AND TABLE_NAME = 'synchronization'
              AND COLUMN_NAME = @name
            """,
            connection);
        command.Parameters.AddWithValue("@name", name);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static bool ColumnNullable(MySqlConnection connection, string name)
    {
        using var command = new MySqlCommand(
            """
            SELECT IS_NULLABLE
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'synchronization'
              AND COLUMN_NAME = @name
            """,
            connection);
        command.Parameters.AddWithValue("@name", name);
        return string.Equals(Convert.ToString(command.ExecuteScalar()), "YES", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ConstraintExists(MySqlConnection connection, string name)
    {
        using var command = new MySqlCommand(
            """
            SELECT COUNT(*)
            FROM information_schema.TABLE_CONSTRAINTS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'synchronization'
              AND CONSTRAINT_NAME = @name
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
              AND TABLE_NAME = 'synchronization'
              AND INDEX_NAME = @name
            """,
            connection);
        command.Parameters.AddWithValue("@name", name);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }
}
