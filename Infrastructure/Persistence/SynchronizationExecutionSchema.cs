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
        if (TableExists(connection, "synchronization_run"))
        {
            return;
        }

        Execute(
            connection,
            """
            CREATE TABLE [dbo].[synchronization_run] (
                [id]                 UNIQUEIDENTIFIER NOT NULL CONSTRAINT [df_synchronization_run_id] DEFAULT NEWSEQUENTIALID(),
                [synchronization_id] INT              NOT NULL,
                [status]             NVARCHAR(16)     NOT NULL,
                [started_at]         DATETIME2(6)     NOT NULL,
                [completed_at]       DATETIME2(6)     NULL,
                [total_records]      INT              NOT NULL CONSTRAINT [df_synchronization_run_total_records] DEFAULT 0,
                [processed_records]  INT              NOT NULL CONSTRAINT [df_synchronization_run_processed_records] DEFAULT 0,
                [success_records]    INT              NOT NULL CONSTRAINT [df_synchronization_run_success_records] DEFAULT 0,
                [failed_records]     INT              NOT NULL CONSTRAINT [df_synchronization_run_failed_records] DEFAULT 0,
                [skipped_records]    INT              NOT NULL CONSTRAINT [df_synchronization_run_skipped_records] DEFAULT 0,
                [retry_count]        INT              NOT NULL CONSTRAINT [df_synchronization_run_retry_count] DEFAULT 0,
                [error_message]      NVARCHAR(MAX)    NULL,
                [created_at]         DATETIME2(6)     NOT NULL CONSTRAINT [df_synchronization_run_created_at] DEFAULT SYSUTCDATETIME(),
                CONSTRAINT [pk_synchronization_run] PRIMARY KEY CLUSTERED ([id]),
                CONSTRAINT [fk_synchronization_run_synchronization]
                    FOREIGN KEY ([synchronization_id]) REFERENCES [dbo].[synchronization] ([id]),
                CONSTRAINT [ck_synchronization_run_status]
                    CHECK ([status] IN (N'pending', N'running', N'success', N'failed')),
                CONSTRAINT [ck_synchronization_run_counts]
                    CHECK (
                        [total_records] >= 0
                        AND [processed_records] >= 0
                        AND [success_records] >= 0
                        AND [failed_records] >= 0
                        AND [skipped_records] >= 0
                        AND [retry_count] >= 0),
                CONSTRAINT [ck_synchronization_run_times]
                    CHECK ([completed_at] IS NULL OR [completed_at] >= [started_at])
            )
            """);
        Execute(
            connection,
            """
            CREATE NONCLUSTERED INDEX [ix_synchronization_run_synchronization]
            ON [dbo].[synchronization_run] ([synchronization_id], [started_at])
            """);
        Execute(
            connection,
            """
            CREATE NONCLUSTERED INDEX [ix_synchronization_run_due]
            ON [dbo].[synchronization_run] ([status], [started_at])
            """);
    }

    private static void EnsureRecordTable(SqlConnection connection)
    {
        if (TableExists(connection, "synchronization_record"))
        {
            return;
        }

        Execute(
            connection,
            """
            CREATE TABLE [dbo].[synchronization_record] (
                [id]                   UNIQUEIDENTIFIER NOT NULL CONSTRAINT [df_synchronization_record_id] DEFAULT NEWSEQUENTIALID(),
                [synchronization_id]   INT              NOT NULL,
                [last_run_id]          UNIQUEIDENTIFIER NOT NULL,
                [source_record_id]     NVARCHAR(128)    NOT NULL,
                [source_business_key]  NVARCHAR(256)    NULL,
                [source_hash]          NVARCHAR(128)    NULL,
                [destination_record_id] NVARCHAR(128)   NULL,
                [status]               NVARCHAR(16)     NOT NULL,
                [attempt_count]        INT              NOT NULL CONSTRAINT [df_synchronization_record_attempt_count] DEFAULT 0,
                [last_attempt_at]      DATETIME2(6)     NULL,
                [last_success_at]      DATETIME2(6)     NULL,
                [error_code]           NVARCHAR(64)     NULL,
                [error_message]        NVARCHAR(MAX)    NULL,
                [created_at]           DATETIME2(6)     NOT NULL CONSTRAINT [df_synchronization_record_created_at] DEFAULT SYSUTCDATETIME(),
                [updated_at]           DATETIME2(6)     NOT NULL CONSTRAINT [df_synchronization_record_updated_at] DEFAULT SYSUTCDATETIME(),
                CONSTRAINT [pk_synchronization_record] PRIMARY KEY CLUSTERED ([id]),
                CONSTRAINT [uq_synchronization_record_source] UNIQUE ([synchronization_id], [source_record_id]),
                CONSTRAINT [fk_synchronization_record_synchronization]
                    FOREIGN KEY ([synchronization_id]) REFERENCES [dbo].[synchronization] ([id]),
                CONSTRAINT [fk_synchronization_record_last_run]
                    FOREIGN KEY ([last_run_id]) REFERENCES [dbo].[synchronization_run] ([id]),
                CONSTRAINT [ck_synchronization_record_status]
                    CHECK ([status] IN (N'pending', N'success', N'failed', N'skipped')),
                CONSTRAINT [ck_synchronization_record_attempt]
                    CHECK ([attempt_count] >= 0)
            )
            """);
        Execute(
            connection,
            """
            CREATE NONCLUSTERED INDEX [ix_synchronization_record_last_run]
            ON [dbo].[synchronization_record] ([last_run_id])
            """);
        Execute(
            connection,
            """
            CREATE NONCLUSTERED INDEX [ix_synchronization_record_status]
            ON [dbo].[synchronization_record] ([synchronization_id], [status])
            """);
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
