using Microsoft.Data.SqlClient;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class MappingTableSchema
{
    public static void Ensure(SqlConnection connection)
    {
        if (!TableExists(connection, "mapping_table"))
        {
            return;
        }

        AddColumn(connection, "entity_code", "NVARCHAR(64) NULL");
        AddColumn(connection, "entity_type", "NVARCHAR(32) NULL");
        AddColumn(connection, "cabinet_code", "NVARCHAR(64) NULL");
        AddColumn(connection, "cabinet_type", "NVARCHAR(32) NULL");
        Backfill(connection, "entity_code", "entity_type", "Sage");
        Backfill(connection, "cabinet_code", "cabinet_type", "Docuware");

        if (Count(connection, "SELECT COUNT(*) FROM mapping_table WHERE entity_code IS NULL OR entity_type IS NULL OR cabinet_code IS NULL OR cabinet_type IS NULL") > 0)
        {
            throw new InvalidOperationException(
                "mapping_table needs a Sage configuration and a DocuWare configuration before code and type can be required.");
        }

        Execute(connection, "ALTER TABLE mapping_table ALTER COLUMN entity_code NVARCHAR(64) NOT NULL");
        Execute(connection, "ALTER TABLE mapping_table ALTER COLUMN entity_type NVARCHAR(32) NOT NULL");
        Execute(connection, "ALTER TABLE mapping_table ALTER COLUMN cabinet_code NVARCHAR(64) NOT NULL");
        Execute(connection, "ALTER TABLE mapping_table ALTER COLUMN cabinet_type NVARCHAR(32) NOT NULL");
        DropPairUniqueUnlessCurrent(connection);
        DropColumn(connection, "entity_config");
        DropColumn(connection, "cabinet_config");
        ReplacePairUnique(connection);
        EnsureIdentityColumns(connection);
    }

    private static void EnsureIdentityColumns(SqlConnection connection)
    {
        AddColumn(connection, "code", "NVARCHAR(64) NULL");
        Execute(
            connection,
            """
            UPDATE mapping_table
            SET code = CONCAT(N'MAP-', id)
            WHERE code IS NULL OR code = N''
            """);
        if (ColumnNullable(connection, "code"))
        {
            Execute(connection, "ALTER TABLE mapping_table ALTER COLUMN code NVARCHAR(64) NOT NULL");
        }

        if (!ConstraintExists(connection, "uq_oc_mapping_table_code"))
        {
            Execute(
                connection,
                """
                ALTER TABLE mapping_table
                ADD CONSTRAINT uq_oc_mapping_table_code UNIQUE (code)
                """);
        }

        AddColumn(connection, "description", "NVARCHAR(512) NULL");
    }

    private static void Backfill(SqlConnection connection, string codeColumn, string typeColumn, string type)
    {
        Execute(
            connection,
            $"""
            UPDATE mapping_table
            SET [{codeColumn}] = picked.code,
                [{typeColumn}] = picked.type
            FROM mapping_table
            CROSS JOIN (
                SELECT TOP (1) code, type
                FROM (
                    SELECT DISTINCT code, type
                    FROM setting
                    WHERE type = N'{type}'
                ) AS configuration
                ORDER BY CASE WHEN code = N'{type}' THEN 0 ELSE 1 END, code
            ) AS picked
            WHERE mapping_table.[{codeColumn}] IS NULL
            """);
    }

    private static void DropPairUniqueUnlessCurrent(SqlConnection connection)
    {
        if (!ConstraintExists(connection, "uq_oc_mapping_table_entity_cabinet"))
        {
            return;
        }

        var columns = ConstraintColumns(connection, "uq_oc_mapping_table_entity_cabinet");
        if (columns.Contains("entity_code", StringComparer.OrdinalIgnoreCase)
            && columns.Contains("entity_type", StringComparer.OrdinalIgnoreCase)
            && columns.Contains("cabinet_code", StringComparer.OrdinalIgnoreCase)
            && columns.Contains("cabinet_type", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        Execute(connection, "ALTER TABLE mapping_table DROP CONSTRAINT uq_oc_mapping_table_entity_cabinet");
    }

    private static void ReplacePairUnique(SqlConnection connection)
    {
        if (ConstraintExists(connection, "uq_oc_mapping_table_entity_cabinet"))
        {
            return;
        }

        Execute(
            connection,
            """
            ALTER TABLE mapping_table
            ADD CONSTRAINT uq_oc_mapping_table_entity_cabinet
            UNIQUE (entity_code, entity_type, entity_name, cabinet_code, cabinet_type, cabinet_name)
            """);
    }

    private static void AddColumn(SqlConnection connection, string name, string definition)
    {
        if (!ColumnExists(connection, name))
        {
            Execute(connection, $"ALTER TABLE mapping_table ADD [{name}] {definition}");
        }
    }

    private static void DropColumn(SqlConnection connection, string name)
    {
        if (ColumnExists(connection, name))
        {
            Execute(connection, $"ALTER TABLE mapping_table DROP COLUMN [{name}]");
        }
    }

    private static void Execute(SqlConnection connection, string sql)
    {
        using var command = new SqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    private static int Count(SqlConnection connection, string sql)
    {
        using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(command.ExecuteScalar());
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

    private static bool ColumnNullable(SqlConnection connection, string name)
    {
        using var command = new SqlCommand(
            """
            SELECT IS_NULLABLE
            FROM information_schema.COLUMNS
            WHERE TABLE_CATALOG = DB_NAME()
              AND TABLE_NAME = 'mapping_table'
              AND COLUMN_NAME = @name
            """,
            connection);
        command.Parameters.AddWithValue("@name", name);
        return string.Equals(Convert.ToString(command.ExecuteScalar()), "YES", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ColumnExists(SqlConnection connection, string name)
    {
        using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM information_schema.COLUMNS
            WHERE TABLE_CATALOG = DB_NAME()
              AND TABLE_NAME = 'mapping_table'
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
              AND TABLE_NAME = 'mapping_table'
              AND CONSTRAINT_NAME = @name
            """,
            connection);
        command.Parameters.AddWithValue("@name", name);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static List<string> ConstraintColumns(SqlConnection connection, string name)
    {
        using var command = new SqlCommand(
            """
            SELECT COLUMN_NAME
            FROM information_schema.CONSTRAINT_COLUMN_USAGE
            WHERE TABLE_CATALOG = DB_NAME()
              AND TABLE_NAME = 'mapping_table'
              AND CONSTRAINT_NAME = @name
            """,
            connection);
        command.Parameters.AddWithValue("@name", name);
        var columns = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }
}
