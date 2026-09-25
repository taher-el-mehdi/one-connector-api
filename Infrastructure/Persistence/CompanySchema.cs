using Microsoft.Data.SqlClient;

namespace DocuWareSageConnector.Infrastructure.Persistence;

/// <summary>
/// Removes the SaaS <c>company</c> tenant. One installation is one customer, so operational
/// tables no longer store a company key.
/// </summary>
internal static class CompanySchema
{
    public static void Remove(SqlConnection connection)
    {
        DropCompanyColumn(connection, "logs");
        DropCompanyColumn(connection, "mapping_field");
        DropCompanyColumn(connection, "mapping_table");
        DropCompanyColumn(connection, "synchronization");
        DropCompanyColumn(connection, "setting");
        DropCompanyColumn(connection, "setting_docuware");
        DropCompanyColumn(connection, "setting_erp");
        DropCompanyColumn(connection, "setting_synchronization");
        EnsureIndexes(connection);
        DropForeignKeysReferencingCompany(connection);
        if (TableExists(connection, "company"))
        {
            Execute(connection, "DROP TABLE company");
        }
    }

    private static void DropCompanyColumn(SqlConnection connection, string table)
    {
        if (!TableExists(connection, table) || !ColumnExists(connection, table, "company"))
        {
            return;
        }

        DeleteDuplicateKeys(connection, table);
        DropForeignKeysOnColumn(connection, table, "company");
        foreach (var index in IndexesUsingColumn(connection, table, "company"))
        {
            Execute(connection, $"ALTER TABLE [{table}] DROP INDEX [{index}]");
        }

        var primaryKey = PrimaryKeyColumns(connection, table);
        if (primaryKey.Contains("company", StringComparer.OrdinalIgnoreCase))
        {
            var remaining = primaryKey.Where(column => !column.Equals("company", StringComparison.OrdinalIgnoreCase)).ToArray();
            Execute(connection, $"ALTER TABLE [{table}] DROP PRIMARY KEY");
            if (remaining.Length > 0)
            {
                var columns = string.Join(", ", remaining.Select(column => $"[{column}]"));
                Execute(connection, $"ALTER TABLE [{table}] ADD PRIMARY KEY ({columns})");
            }
        }

        Execute(connection, $"ALTER TABLE [{table}] DROP COLUMN [company]");
    }

    private static void DeleteDuplicateKeys(SqlConnection connection, string table)
    {
        var sql = table switch
        {
            "setting" =>
                """
                DELETE older FROM setting AS older
                INNER JOIN setting AS keeper
                  ON keeper.type = older.type
                 AND keeper.code = older.code
                 AND keeper.[key] = older.[key]
                 AND keeper.company < older.company
                """,
            "setting_docuware" or "setting_erp" or "setting_synchronization" =>
                $"""
                DELETE older FROM [{table}] AS older
                INNER JOIN [{table}] AS keeper
                  ON keeper.[key] = older.[key]
                 AND keeper.company < older.company
                """,
            "mapping_table" =>
                """
                DELETE older FROM mapping_table AS older
                INNER JOIN mapping_table AS keeper
                  ON keeper.entity_name = older.entity_name
                 AND keeper.cabinet_name = older.cabinet_name
                 AND keeper.id < older.id
                """,
            "synchronization" when ColumnExists(connection, "synchronization", "code") =>
                """
                DELETE older FROM synchronization AS older
                INNER JOIN synchronization AS keeper
                  ON keeper.code = older.code
                 AND keeper.id < older.id
                """,
            _ => null
        };
        if (sql is not null)
        {
            Execute(connection, sql);
        }
    }

    private static void EnsureIndexes(SqlConnection connection)
    {
        if (TableExists(connection, "logs"))
        {
            EnsureIndex(connection, "logs", "ix_logs_updated", "ALTER TABLE logs ADD KEY ix_logs_updated (updated_at)");
            EnsureIndex(connection, "logs", "ix_logs_status", "ALTER TABLE logs ADD KEY ix_logs_status (status, updated_at)");
            EnsureIndex(connection, "logs", "ix_logs_synchronization", "ALTER TABLE logs ADD KEY ix_logs_synchronization (id_synchronization)");
        }

        if (TableExists(connection, "mapping_table"))
        {
            EnsureIndex(
                connection,
                "mapping_table",
                "uq_oc_mapping_table_entity_cabinet",
                "ALTER TABLE mapping_table ADD UNIQUE KEY uq_oc_mapping_table_entity_cabinet (entity_name, cabinet_name)");
        }

        if (TableExists(connection, "synchronization") && ColumnExists(connection, "synchronization", "code"))
        {
            EnsureIndex(
                connection,
                "synchronization",
                "uq_synchronization_code",
                "ALTER TABLE synchronization ADD UNIQUE KEY uq_synchronization_code (code)");
        }

    }

    private static void EnsureIndex(SqlConnection connection, string table, string index, string sql)
    {
        if (!IndexExists(connection, table, index))
        {
            Execute(connection, sql);
        }
    }

    private static void DropForeignKeysReferencingCompany(SqlConnection connection)
    {
        using var command = new SqlCommand(
            """
            SELECT OBJECT_NAME(fk.parent_object_id) AS TABLE_NAME, fk.name AS CONSTRAINT_NAME
            FROM sys.foreign_keys AS fk
            INNER JOIN sys.tables AS referenced ON referenced.object_id = fk.referenced_object_id
            WHERE referenced.name = 'company'
            """,
            connection);
        var keys = new List<(string Table, string Constraint)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                keys.Add((reader.GetString("TABLE_NAME"), reader.GetString("CONSTRAINT_NAME")));
            }
        }

        foreach (var (table, constraint) in keys)
        {
            Execute(connection, $"ALTER TABLE [{table}] DROP CONSTRAINT [{constraint}]");
        }
    }

    private static void DropForeignKeysOnColumn(SqlConnection connection, string table, string column)
    {
        using var command = new SqlCommand(
            """
            SELECT CONSTRAINT_NAME
            FROM information_schema.KEY_COLUMN_USAGE
            WHERE TABLE_CATALOG = DB_NAME()
              AND TABLE_NAME = @table
              AND COLUMN_NAME = @column
              AND REFERENCED_TABLE_NAME IS NOT NULL
            """,
            connection);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        var names = new List<string>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                names.Add(reader.GetString("CONSTRAINT_NAME"));
            }
        }

        foreach (var name in names.Distinct(StringComparer.Ordinal))
        {
            Execute(connection, $"ALTER TABLE [{table}] DROP FOREIGN KEY [{name}]");
        }
    }

    private static IReadOnlyList<string> IndexesUsingColumn(SqlConnection connection, string table, string column)
    {
        using var command = new SqlCommand(
            """
            SELECT DISTINCT i.name AS INDEX_NAME
            FROM sys.indexes AS i
            INNER JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            INNER JOIN sys.tables AS t ON t.object_id = i.object_id
            WHERE t.name = @table
              AND c.name = @column
              AND i.is_primary_key = 0
            """,
            connection);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString("INDEX_NAME"));
        }

        return names;
    }

    private static IReadOnlyList<string> PrimaryKeyColumns(SqlConnection connection, string table)
    {
        using var command = new SqlCommand(
            """
            SELECT c.name AS COLUMN_NAME
            FROM sys.indexes AS i
            INNER JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            INNER JOIN sys.tables AS t ON t.object_id = i.object_id
            WHERE t.name = @table
              AND i.is_primary_key = 1
            ORDER BY ic.key_ordinal
            """,
            connection);
        command.Parameters.AddWithValue("@table", table);
        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString("COLUMN_NAME"));
        }

        return names;
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
            FROM information_schema.TABLES
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
            FROM information_schema.COLUMNS
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
