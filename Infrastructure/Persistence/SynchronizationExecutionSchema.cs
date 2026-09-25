using Microsoft.Data.SqlClient;

namespace DocuWareSageConnector.Infrastructure.Persistence;

/// <summary>
/// Moves synchronization execution off the definition table.
/// <c>synchronization</c> keeps configuration. One run is <c>synchronization_run</c>.
/// One Sage record is <c>synchronization_record</c>, unique per synchronization.
/// </summary>
internal static class SynchronizationExecutionSchema
{
    public static void Ensure(SqlConnection connection)
    {
        if (!TableExists(connection, "synchronization"))
        {
            return;
        }

        EnsureRunTable(connection);
        EnsureRecordTable(connection);
        DropRuntimeColumns(connection);
    }

    private static void EnsureRunTable(SqlConnection connection)
    {
        if (!TableExists(connection, "synchronization_run"))
        {
            CreateRunTable(connection);
            return;
        }

        ReshapeRunTable(connection);
    }

    private static void CreateRunTable(SqlConnection connection)
    {
        Execute(
            connection,
            """
            CREATE TABLE [dbo].[synchronization_run] (
                [id]                 UNIQUEIDENTIFIER NOT NULL CONSTRAINT [df_synchronization_run_id] DEFAULT NEWSEQUENTIALID(),
                [synchronization_id] INT              NOT NULL,
                [status]             NVARCHAR(16)     NOT NULL,
                [start_at]           DATETIME2(6)     NOT NULL,
                [completed_at]       DATETIME2(6)     NULL,
                [records_inserted]   INT              NOT NULL CONSTRAINT [df_synchronization_run_records_inserted] DEFAULT 0,
                [records_updated]    INT              NOT NULL CONSTRAINT [df_synchronization_run_records_updated] DEFAULT 0,
                [error_message]      NVARCHAR(MAX)    NULL,
                [created_at]         DATETIME2(6)     NOT NULL CONSTRAINT [df_synchronization_run_created_at] DEFAULT SYSUTCDATETIME(),
                CONSTRAINT [pk_synchronization_run] PRIMARY KEY CLUSTERED ([id]),
                CONSTRAINT [fk_synchronization_run_synchronization]
                    FOREIGN KEY ([synchronization_id]) REFERENCES [dbo].[synchronization] ([id]),
                CONSTRAINT [ck_synchronization_run_status]
                    CHECK ([status] IN (N'pending', N'running', N'success', N'failed')),
                CONSTRAINT [ck_synchronization_run_counts]
                    CHECK ([records_inserted] >= 0 AND [records_updated] >= 0),
                CONSTRAINT [ck_synchronization_run_times]
                    CHECK ([completed_at] IS NULL OR [completed_at] >= [start_at])
            )
            """);
        Execute(
            connection,
            """
            CREATE NONCLUSTERED INDEX [ix_synchronization_run_synchronization]
            ON [dbo].[synchronization_run] ([synchronization_id], [start_at])
            """);
        Execute(
            connection,
            """
            CREATE NONCLUSTERED INDEX [ix_synchronization_run_due]
            ON [dbo].[synchronization_run] ([status], [start_at])
            """);
    }

    private static void ReshapeRunTable(SqlConnection connection)
    {
        if (ColumnExists(connection, "synchronization_run", "started_at")
            && !ColumnExists(connection, "synchronization_run", "start_at"))
        {
            DropRunConstraint(connection, "ck_synchronization_run_times");
            DropCheckConstraintsOnColumn(connection, "synchronization_run", "started_at");
            foreach (var index in IndexesOnColumn(connection, "synchronization_run", "started_at"))
            {
                Execute(connection, $"DROP INDEX [{index}] ON [dbo].[synchronization_run]");
            }

            Execute(connection, "EXEC sp_rename 'dbo.synchronization_run.started_at', 'start_at', 'COLUMN'");
        }

        DropRunConstraint(connection, "ck_synchronization_run_counts");
        DropRunConstraint(connection, "ck_synchronization_run_times");
        EnsureRunCountColumn(connection, "records_inserted", "df_synchronization_run_records_inserted");
        EnsureRunCountColumn(connection, "records_updated", "df_synchronization_run_records_updated");
        DropRunColumn(connection, "total_records");
        DropRunColumn(connection, "processed_records");
        DropRunColumn(connection, "success_records");
        DropRunColumn(connection, "failed_records");
        DropRunColumn(connection, "skipped_records");
        DropRunColumn(connection, "retry_count");

        if (!ConstraintExists(connection, "synchronization_run", "ck_synchronization_run_counts"))
        {
            Execute(
                connection,
                """
                ALTER TABLE [dbo].[synchronization_run]
                ADD CONSTRAINT [ck_synchronization_run_counts]
                    CHECK ([records_inserted] >= 0 AND [records_updated] >= 0)
                """);
        }

        if (!ConstraintExists(connection, "synchronization_run", "ck_synchronization_run_times"))
        {
            Execute(
                connection,
                """
                ALTER TABLE [dbo].[synchronization_run]
                ADD CONSTRAINT [ck_synchronization_run_times]
                    CHECK ([completed_at] IS NULL OR [completed_at] >= [start_at])
                """);
        }

        EnsureRunIndex(
            connection,
            "ix_synchronization_run_synchronization",
            "([synchronization_id], [start_at])");
        EnsureRunIndex(
            connection,
            "ix_synchronization_run_due",
            "([status], [start_at])");
    }

    private static void EnsureRunIndex(SqlConnection connection, string index, string columns)
    {
        if (IndexExists(connection, "synchronization_run", index))
        {
            return;
        }

        Execute(
            connection,
            $"""
            CREATE NONCLUSTERED INDEX [{index}]
            ON [dbo].[synchronization_run] {columns}
            """);
    }

    private static void DropCheckConstraintsOnColumn(SqlConnection connection, string table, string column)
    {
        using var command = new SqlCommand(
            """
            SELECT cc.name
            FROM sys.check_constraints AS cc
            INNER JOIN sys.tables AS t ON t.object_id = cc.parent_object_id
            WHERE t.name = @table
              AND CHARINDEX(@column, cc.definition) > 0
            """,
            connection);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        var names = new List<string>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }
        }

        foreach (var name in names)
        {
            Execute(connection, $"ALTER TABLE [dbo].[{table}] DROP CONSTRAINT [{name}]");
        }
    }

    private static void EnsureRunCountColumn(SqlConnection connection, string column, string defaultName)
    {
        if (ColumnExists(connection, "synchronization_run", column))
        {
            return;
        }

        Execute(
            connection,
            $"""
            ALTER TABLE [dbo].[synchronization_run]
            ADD [{column}] INT NOT NULL CONSTRAINT [{defaultName}] DEFAULT 0
            """);
    }

    private static void DropRunColumn(SqlConnection connection, string column)
    {
        if (!ColumnExists(connection, "synchronization_run", column))
        {
            return;
        }

        foreach (var index in IndexesOnColumn(connection, "synchronization_run", column))
        {
            Execute(connection, $"DROP INDEX [{index}] ON [dbo].[synchronization_run]");
        }

        var constraint = DefaultConstraint(connection, "synchronization_run", column);
        if (constraint is not null)
        {
            Execute(connection, $"ALTER TABLE [dbo].[synchronization_run] DROP CONSTRAINT [{constraint}]");
        }

        Execute(connection, $"ALTER TABLE [dbo].[synchronization_run] DROP COLUMN [{column}]");
    }

    private static void DropRunConstraint(SqlConnection connection, string name)
    {
        if (ConstraintExists(connection, "synchronization_run", name))
        {
            Execute(connection, $"ALTER TABLE [dbo].[synchronization_run] DROP CONSTRAINT [{name}]");
        }
    }

    private static void EnsureRecordTable(SqlConnection connection)
    {
        if (!TableExists(connection, "synchronization_record"))
        {
            CreateRecordTable(connection);
            return;
        }

        ReshapeRecordTable(connection);
    }

    private static void CreateRecordTable(SqlConnection connection)
    {
        Execute(
            connection,
            """
            CREATE TABLE [dbo].[synchronization_record] (
                [id]                   UNIQUEIDENTIFIER NOT NULL CONSTRAINT [df_synchronization_record_id] DEFAULT NEWSEQUENTIALID(),
                [synchronization_id]   INT              NOT NULL,
                [last_run_id]          UNIQUEIDENTIFIER NOT NULL,
                [entity_id]            NVARCHAR(512)    NOT NULL,
                [docuware_id]          NVARCHAR(128)    NULL,
                [error]                NVARCHAR(MAX)    NULL,
                [created_at]           DATETIME2(6)     NOT NULL CONSTRAINT [df_synchronization_record_created_at] DEFAULT SYSUTCDATETIME(),
                [updated_at]           DATETIME2(6)     NOT NULL CONSTRAINT [df_synchronization_record_updated_at] DEFAULT SYSUTCDATETIME(),
                CONSTRAINT [pk_synchronization_record] PRIMARY KEY CLUSTERED ([id]),
                CONSTRAINT [uq_synchronization_record_entity] UNIQUE ([synchronization_id], [entity_id]),
                CONSTRAINT [fk_synchronization_record_synchronization]
                    FOREIGN KEY ([synchronization_id]) REFERENCES [dbo].[synchronization] ([id]),
                CONSTRAINT [fk_synchronization_record_last_run]
                    FOREIGN KEY ([last_run_id]) REFERENCES [dbo].[synchronization_run] ([id])
            )
            """);
        Execute(
            connection,
            """
            CREATE NONCLUSTERED INDEX [ix_synchronization_record_last_run]
            ON [dbo].[synchronization_record] ([last_run_id])
            """);
    }

    private static void ReshapeRecordTable(SqlConnection connection)
    {
        if (!ColumnExists(connection, "synchronization_record", "entity_id"))
        {
            Execute(connection, "ALTER TABLE [dbo].[synchronization_record] ADD [entity_id] NVARCHAR(512) NULL");
            if (ColumnExists(connection, "synchronization_record", "source_record_id"))
            {
                Execute(
                    connection,
                    """
                    UPDATE [dbo].[synchronization_record]
                    SET [entity_id] = [source_record_id]
                    WHERE [entity_id] IS NULL
                    """);
            }

            Execute(
                connection,
                """
                DELETE FROM [dbo].[synchronization_record]
                WHERE [entity_id] IS NULL OR LTRIM(RTRIM([entity_id])) = N''
                """);
            Execute(connection, "ALTER TABLE [dbo].[synchronization_record] ALTER COLUMN [entity_id] NVARCHAR(512) NOT NULL");
        }

        if (ColumnExists(connection, "synchronization_record", "destination_record_id")
            && !ColumnExists(connection, "synchronization_record", "docuware_id"))
        {
            Execute(
                connection,
                "EXEC sp_rename 'dbo.synchronization_record.destination_record_id', 'docuware_id', 'COLUMN'");
        }
        else if (!ColumnExists(connection, "synchronization_record", "docuware_id"))
        {
            Execute(connection, "ALTER TABLE [dbo].[synchronization_record] ADD [docuware_id] NVARCHAR(128) NULL");
        }

        if (!ColumnExists(connection, "synchronization_record", "error"))
        {
            Execute(connection, "ALTER TABLE [dbo].[synchronization_record] ADD [error] NVARCHAR(MAX) NULL");
        }

        if (ColumnExists(connection, "synchronization_record", "error_code")
            || ColumnExists(connection, "synchronization_record", "error_message"))
        {
            var code = ColumnExists(connection, "synchronization_record", "error_code")
                ? "NULLIF(LTRIM(RTRIM([error_code])), N'')"
                : "CAST(NULL AS NVARCHAR(64))";
            var message = ColumnExists(connection, "synchronization_record", "error_message")
                ? "NULLIF(LTRIM(RTRIM([error_message])), N'')"
                : "CAST(NULL AS NVARCHAR(MAX))";
            Execute(
                connection,
                $"""
                UPDATE [dbo].[synchronization_record]
                SET [error] = CASE
                    WHEN {code} IS NOT NULL AND {message} IS NOT NULL THEN {code} + N': ' + {message}
                    ELSE COALESCE({message}, {code})
                END
                WHERE [error] IS NULL
                """);
        }

        DropRecordIndex(connection, "ix_synchronization_record_status");
        DropRecordConstraint(connection, "ck_synchronization_record_status");
        DropRecordConstraint(connection, "ck_synchronization_record_attempt");
        DropRecordConstraint(connection, "uq_synchronization_record_source");
        DropRecordColumn(connection, "status");
        DropRecordColumn(connection, "source_business_key");
        DropRecordColumn(connection, "attempt_count");
        DropRecordColumn(connection, "last_attempt_at");
        DropRecordColumn(connection, "last_success_at");
        DropRecordColumn(connection, "source_record_id");
        DropRecordColumn(connection, "source_hash");
        DropRecordColumn(connection, "error_code");
        DropRecordColumn(connection, "error_message");

        if (!ConstraintExists(connection, "synchronization_record", "uq_synchronization_record_entity"))
        {
            Execute(
                connection,
                """
                ALTER TABLE [dbo].[synchronization_record]
                ADD CONSTRAINT [uq_synchronization_record_entity] UNIQUE ([synchronization_id], [entity_id])
                """);
        }

        if (!IndexExists(connection, "synchronization_record", "ix_synchronization_record_last_run"))
        {
            Execute(
                connection,
                """
                CREATE NONCLUSTERED INDEX [ix_synchronization_record_last_run]
                ON [dbo].[synchronization_record] ([last_run_id])
                """);
        }
    }

    private static void DropRecordColumn(SqlConnection connection, string column)
    {
        if (!ColumnExists(connection, "synchronization_record", column))
        {
            return;
        }

        foreach (var index in IndexesOnColumn(connection, "synchronization_record", column))
        {
            Execute(connection, $"DROP INDEX [{index}] ON [dbo].[synchronization_record]");
        }

        var constraint = DefaultConstraint(connection, "synchronization_record", column);
        if (constraint is not null)
        {
            Execute(connection, $"ALTER TABLE [dbo].[synchronization_record] DROP CONSTRAINT [{constraint}]");
        }

        Execute(connection, $"ALTER TABLE [dbo].[synchronization_record] DROP COLUMN [{column}]");
    }

    private static void DropRecordIndex(SqlConnection connection, string index)
    {
        if (IndexExists(connection, "synchronization_record", index))
        {
            Execute(connection, $"DROP INDEX [{index}] ON [dbo].[synchronization_record]");
        }
    }

    private static void DropRecordConstraint(SqlConnection connection, string name)
    {
        if (ConstraintExists(connection, "synchronization_record", name))
        {
            Execute(connection, $"ALTER TABLE [dbo].[synchronization_record] DROP CONSTRAINT [{name}]");
        }
    }

    private static bool ConstraintExists(SqlConnection connection, string table, string name)
    {
        using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
            WHERE TABLE_CATALOG = DB_NAME()
              AND TABLE_NAME = @table
              AND CONSTRAINT_NAME = @name
            """,
            connection);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@name", name);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static void DropRuntimeColumns(SqlConnection connection)
    {
        DropIndex(connection, "ix_synchronization_due");
        DropColumn(connection, "next_run_at");
        DropColumn(connection, "retry_count");
    }

    private static void DropColumn(SqlConnection connection, string column)
    {
        if (!ColumnExists(connection, "synchronization", column))
        {
            return;
        }

        foreach (var index in IndexesOnColumn(connection, "synchronization", column))
        {
            Execute(connection, $"DROP INDEX [{index}] ON [dbo].[synchronization]");
        }

        var constraint = DefaultConstraint(connection, "synchronization", column);
        if (constraint is not null)
        {
            Execute(connection, $"ALTER TABLE [dbo].[synchronization] DROP CONSTRAINT [{constraint}]");
        }

        Execute(connection, $"ALTER TABLE [dbo].[synchronization] DROP COLUMN [{column}]");
    }

    private static void DropIndex(SqlConnection connection, string index)
    {
        if (IndexExists(connection, "synchronization", index))
        {
            Execute(connection, $"DROP INDEX [{index}] ON [dbo].[synchronization]");
        }
    }

    private static IReadOnlyList<string> IndexesOnColumn(SqlConnection connection, string table, string column)
    {
        using var command = new SqlCommand(
            """
            SELECT DISTINCT i.name
            FROM sys.indexes AS i
            INNER JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            INNER JOIN sys.tables AS t ON t.object_id = i.object_id
            WHERE t.name = @table
              AND c.name = @column
              AND i.is_primary_key = 0
              AND i.is_unique_constraint = 0
              AND i.name IS NOT NULL
            """,
            connection);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static string? DefaultConstraint(SqlConnection connection, string table, string column)
    {
        using var command = new SqlCommand(
            """
            SELECT dc.name
            FROM sys.default_constraints AS dc
            INNER JOIN sys.columns AS c ON c.default_object_id = dc.object_id
            INNER JOIN sys.tables AS t ON t.object_id = c.object_id
            WHERE t.name = @table AND c.name = @column
            """,
            connection);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        return command.ExecuteScalar() as string;
    }

    private static void Execute(SqlConnection connection, string sql)
    {
        using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        command.ExecuteNonQuery();
    }

    private static bool TableExists(SqlConnection connection, string table)
    {
        using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_CATALOG = DB_NAME() AND TABLE_NAME = @table
            """,
            connection);
        command.Parameters.AddWithValue("@table", table);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static bool ColumnExists(SqlConnection connection, string table, string column)
    {
        using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_CATALOG = DB_NAME()
              AND TABLE_NAME = @table
              AND COLUMN_NAME = @column
            """,
            connection);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static bool IndexExists(SqlConnection connection, string table, string index)
    {
        using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM sys.indexes AS i
            INNER JOIN sys.tables AS t ON t.object_id = i.object_id
            WHERE t.name = @table AND i.name = @index
            """,
            connection);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@index", index);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }
}
