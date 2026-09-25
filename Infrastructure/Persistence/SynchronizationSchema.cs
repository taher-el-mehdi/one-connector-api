using Microsoft.Data.SqlClient;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class SynchronizationSchema
{
    public static void Ensure(SqlConnection connection)
    {
        if (!TableExists(connection, "synchronization"))
        {
            return;
        }

        EnsureMappingColumn(connection);
        EnsureQueueColumns(connection);
        EnsureRecurrenceColumns(connection);
    }

    private static void EnsureRecurrenceColumns(SqlConnection connection)
    {
        AddBit(connection, "recurrence_enabled", "df_synchronization_recurrence_enabled");
        AddNullable(connection, "recurrence_type", "NVARCHAR(16) NULL");
        AddBit(connection, "recurrence_mondays", "df_synchronization_recurrence_mondays");
        AddBit(connection, "recurrence_tuesdays", "df_synchronization_recurrence_tuesdays");
        AddBit(connection, "recurrence_wednesdays", "df_synchronization_recurrence_wednesdays");
        AddBit(connection, "recurrence_thursdays", "df_synchronization_recurrence_thursdays");
        AddBit(connection, "recurrence_fridays", "df_synchronization_recurrence_fridays");
        AddBit(connection, "recurrence_saturdays", "df_synchronization_recurrence_saturdays");
        AddBit(connection, "recurrence_sundays", "df_synchronization_recurrence_sundays");
        CopyLegacyWeekdays(connection);
        DropColumn(connection, "recurrence_days");
        AddNullable(connection, "recurrence_time", "TIME NULL");
        AddNullable(connection, "interval_value", "INT NULL");
        AddNullable(connection, "interval_unit", "NVARCHAR(16) NULL");
        AddNullable(connection, "timezone", "NVARCHAR(64) NULL");
        if (!ConstraintExists(connection, "ck_synchronization_recurrence"))
        {
            Execute(
                connection,
                """
                ALTER TABLE [dbo].[synchronization]
                ADD CONSTRAINT [ck_synchronization_recurrence]
                CHECK (
                    [recurrence_enabled] = 0
                    OR (
                        [recurrence_type] IN (N'DAILY', N'WEEKLY', N'INTERVAL')
                        AND [timezone] IS NOT NULL
                        AND (
                            ([recurrence_type] IN (N'DAILY', N'WEEKLY') AND [recurrence_time] IS NOT NULL)
                            OR (
                                [recurrence_type] = N'INTERVAL'
                                AND [interval_value] > 0
                                AND [interval_unit] IN (N'MINUTE', N'HOUR', N'DAY')
                            )
                        )
                    )
                )
                """);
        }
    }

    private static void CopyLegacyWeekdays(SqlConnection connection)
    {
        if (!ColumnExists(connection, "recurrence_days"))
        {
            return;
        }

        Execute(
            connection,
            """
            UPDATE [dbo].[synchronization]
            SET
                [recurrence_mondays] = CASE WHEN [recurrence_days] LIKE N'%MONDAY%' THEN 1 ELSE [recurrence_mondays] END,
                [recurrence_tuesdays] = CASE WHEN [recurrence_days] LIKE N'%TUESDAY%' THEN 1 ELSE [recurrence_tuesdays] END,
                [recurrence_wednesdays] = CASE WHEN [recurrence_days] LIKE N'%WEDNESDAY%' THEN 1 ELSE [recurrence_wednesdays] END,
                [recurrence_thursdays] = CASE WHEN [recurrence_days] LIKE N'%THURSDAY%' THEN 1 ELSE [recurrence_thursdays] END,
                [recurrence_fridays] = CASE WHEN [recurrence_days] LIKE N'%FRIDAY%' THEN 1 ELSE [recurrence_fridays] END,
                [recurrence_saturdays] = CASE WHEN [recurrence_days] LIKE N'%SATURDAY%' THEN 1 ELSE [recurrence_saturdays] END,
                [recurrence_sundays] = CASE WHEN [recurrence_days] LIKE N'%SUNDAY%' THEN 1 ELSE [recurrence_sundays] END
            WHERE [recurrence_days] IS NOT NULL
            """);
    }

    private static void DropColumn(SqlConnection connection, string name)
    {
        if (!ColumnExists(connection, name))
        {
            return;
        }

        Execute(connection, $"ALTER TABLE [dbo].[synchronization] DROP COLUMN [{name}]");
    }

    private static void AddBit(SqlConnection connection, string name, string defaultName)
    {
        if (ColumnExists(connection, name))
        {
            return;
        }

        Execute(
            connection,
            $"ALTER TABLE [dbo].[synchronization] ADD [{name}] BIT NOT NULL CONSTRAINT [{defaultName}] DEFAULT 0");
    }

    private static void AddNullable(SqlConnection connection, string name, string definition)
    {
        if (!ColumnExists(connection, name))
        {
            Execute(connection, $"ALTER TABLE [dbo].[synchronization] ADD [{name}] {definition}");
        }
    }

    private static void EnsureMappingColumn(SqlConnection connection)
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

    private static void EnsureQueueColumns(SqlConnection connection)
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

        if (IndexExists(connection, "uq_synchronization_company_code"))
        {
            Execute(connection, "ALTER TABLE synchronization DROP INDEX uq_synchronization_company_code");
        }

        if (!IndexExists(connection, "uq_synchronization_code"))
        {
            Execute(connection, "ALTER TABLE synchronization ADD UNIQUE KEY uq_synchronization_code (code)");
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

    private static bool RowsMissingUniqueMapping(SqlConnection connection)
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

    private static int Count(SqlConnection connection, string sql)
    {
        using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void Execute(SqlConnection connection, string sql)
    {
        using var command = new SqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    private static bool TableExists(SqlConnection connection, string table)
    {
        using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM information_schema.TABLES
            WHERE TABLE_CATALOG = DB_NAME() AND TABLE_NAME = @table
            """,
            connection);
        command.Parameters.AddWithValue("@table", table);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static bool ColumnExists(SqlConnection connection, string name)
    {
        using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM information_schema.COLUMNS
            WHERE TABLE_CATALOG = DB_NAME()
              AND TABLE_NAME = 'synchronization'
              AND COLUMN_NAME = @name
            """,
            connection);
        command.Parameters.AddWithValue("@name", name);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static bool ColumnNullable(SqlConnection connection, string name)
    {
        using var command = new SqlCommand(
            """
            SELECT IS_NULLABLE
            FROM information_schema.COLUMNS
            WHERE TABLE_CATALOG = DB_NAME()
              AND TABLE_NAME = 'synchronization'
              AND COLUMN_NAME = @name
            """,
            connection);
        command.Parameters.AddWithValue("@name", name);
        return string.Equals(Convert.ToString(command.ExecuteScalar()), "YES", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ConstraintExists(SqlConnection connection, string name)
    {
        using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM information_schema.TABLE_CONSTRAINTS
            WHERE TABLE_CATALOG = DB_NAME()
              AND TABLE_NAME = 'synchronization'
              AND CONSTRAINT_NAME = @name
            """,
            connection);
        command.Parameters.AddWithValue("@name", name);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static bool IndexExists(SqlConnection connection, string name)
    {
        using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.synchronization') AND name = @name
            """,
            connection);
        command.Parameters.AddWithValue("@name", name);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }
}
