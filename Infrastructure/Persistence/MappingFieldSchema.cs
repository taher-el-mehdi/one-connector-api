using Microsoft.Data.SqlClient;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class MappingFieldSchema
{
    public static void Ensure(SqlConnection connection)
    {
        if (!TableExists(connection, "mapping_field"))
        {
            CreateTable(connection);
            return;
        }

        if (!ColumnExists(connection, "id_mapping_table"))
        {
            RenameMappingColumn(connection);
        }

        EnsureKeyColumns(connection);
    }

    private static void RenameMappingColumn(SqlConnection connection)
    {

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

    private static void CreateTable(SqlConnection connection)
    {
        Execute(
            connection,
            """
            CREATE TABLE [dbo].[mapping_field] (
                [id]                 INT IDENTITY(1,1) NOT NULL,
                [id_mapping_table]   INT           NOT NULL,
                [entity_field_name]  NVARCHAR(256) NOT NULL,
                [cabinet_field_name] NVARCHAR(256) NOT NULL,
                [entity_type_name]   NVARCHAR(128) NULL,
                [cabinet_type_name]  NVARCHAR(128) NULL,
                [entity_type_long]   INT           NULL,
                [cabinet_type_long]  INT           NULL,
                [is_key]             BIT           NOT NULL CONSTRAINT [DF_mapping_field_is_key] DEFAULT 0,
                [key_order]          INT           NULL,
                [created_by]         CHAR(36)      NOT NULL CONSTRAINT [df_mapping_field_created_by] DEFAULT '00000000-0000-0000-0000-000000000000',
                [created_at]         DATETIME2(6)  NOT NULL CONSTRAINT [df_mapping_field_created_at] DEFAULT SYSUTCDATETIME(),
                [updated_at]         DATETIME2(6)  NOT NULL CONSTRAINT [df_mapping_field_updated_at] DEFAULT SYSUTCDATETIME(),
                [updated_by]         CHAR(36)      NOT NULL CONSTRAINT [df_mapping_field_updated_by] DEFAULT '00000000-0000-0000-0000-000000000000',
                CONSTRAINT [pk_mapping_field] PRIMARY KEY CLUSTERED ([id]),
                CONSTRAINT [uq_oc_mapping_field_entity_field] UNIQUE ([id_mapping_table], [entity_field_name]),
                CONSTRAINT [fk_mapping_field_table]
                    FOREIGN KEY ([id_mapping_table]) REFERENCES [dbo].[mapping_table] ([id])
                    ON DELETE CASCADE
            )
            """);
    }

    private static void EnsureKeyColumns(SqlConnection connection)
    {
        var hasKey = ColumnExists(connection, "is_key");
        var hasOrder = ColumnExists(connection, "key_order");
        if (hasKey && hasOrder)
        {
            return;
        }

        if (!hasKey && !hasOrder)
        {
            Execute(
                connection,
                """
                ALTER TABLE dbo.mapping_field
                ADD
                    is_key BIT NOT NULL CONSTRAINT DF_mapping_field_is_key DEFAULT 0,
                    key_order INT NULL
                """);
            return;
        }

        if (!hasKey)
        {
            Execute(
                connection,
                "ALTER TABLE dbo.mapping_field ADD is_key BIT NOT NULL CONSTRAINT DF_mapping_field_is_key DEFAULT 0");
        }

        if (!hasOrder)
        {
            Execute(connection, "ALTER TABLE dbo.mapping_field ADD key_order INT NULL");
        }
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
              AND TABLE_NAME = 'mapping_field'
              AND COLUMN_NAME = @name
            """,
            connection);
        command.Parameters.AddWithValue("@name", name);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static bool ConstraintExists(SqlConnection connection, string name)
    {
        using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM information_schema.TABLE_CONSTRAINTS
            WHERE TABLE_CATALOG = DB_NAME()
              AND TABLE_NAME = 'mapping_field'
              AND CONSTRAINT_NAME = @name
            """,
            connection);
        command.Parameters.AddWithValue("@name", name);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }
}
