using System.Text.RegularExpressions;
using DocuWareSageConnector.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DocuWareSageConnector.Sage.Sql;

public sealed class SageMappedTableReader
{
    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    private readonly ISageSqlConnectionFactory _connections;
    private readonly SageOptions _options;

    public SageMappedTableReader(ISageSqlConnectionFactory connections, IOptions<SageOptions> options)
    {
        _connections = connections;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<string>> PrimaryKeyAsync(
        SqlConnection connection,
        int commandTimeoutSeconds,
        string entityName,
        CancellationToken cancellationToken)
    {
        var (schema, table) = SplitEntityName(entityName);
        const string sql = """
            SELECT c.COLUMN_NAME
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS AS tc
            INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE AS c
                ON c.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
               AND c.TABLE_SCHEMA = tc.TABLE_SCHEMA
               AND c.TABLE_NAME = tc.TABLE_NAME
               AND c.CONSTRAINT_CATALOG = tc.CONSTRAINT_CATALOG
            WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
              AND tc.TABLE_SCHEMA = @schema
              AND tc.TABLE_NAME = @table
            ORDER BY c.ORDINAL_POSITION
            """;
        var columns = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = commandTimeoutSeconds;
        command.Parameters.Add(new SqlParameter("@schema", schema));
        command.Parameters.Add(new SqlParameter("@table", table));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            if (Identifier.IsMatch(name))
            {
                columns.Add(name);
            }
        }

        return columns;
    }

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadAsync(
        string entityName,
        IReadOnlyList<string> columns,
        CancellationToken cancellationToken) =>
        ReadAsync(_connections.Create(), _options.CommandTimeoutSeconds, entityName, columns, string.Empty, [], cancellationToken, ownsConnection: true);

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadAsync(
        SqlConnection connection,
        int commandTimeoutSeconds,
        string entityName,
        IReadOnlyList<string> columns,
        string whereSql,
        IReadOnlyList<SqlParameter> whereParameters,
        CancellationToken cancellationToken) =>
        ReadAsync(connection, commandTimeoutSeconds, entityName, columns, whereSql, whereParameters, cancellationToken, ownsConnection: false);

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadAsync(
        SqlConnection connection,
        int commandTimeoutSeconds,
        string entityName,
        IReadOnlyList<string> columns,
        string whereSql,
        IReadOnlyList<SqlParameter> whereParameters,
        CancellationToken cancellationToken,
        bool ownsConnection)
    {
        var (schema, table) = SplitEntityName(entityName);
        var selected = columns
            .Select(column => column.Trim())
            .Where(column => column.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selected.Length == 0)
        {
            throw new InvalidOperationException("The mapping has no Sage columns.");
        }

        foreach (var column in selected)
        {
            RequireIdentifier(column, "column");
        }

        var list = string.Join(", ", selected.Select(column => $"[{column}]"));
        var sql = $"SELECT {list} FROM [{schema}].[{table}]";
        if (!string.IsNullOrWhiteSpace(whereSql))
        {
            sql += " WHERE " + whereSql;
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        if (ownsConnection)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = commandTimeoutSeconds;
            foreach (var parameter in whereParameters)
            {
                command.Parameters.Add(parameter);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    values[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index);
                }

                rows.Add(values);
            }

            return rows;
        }
        finally
        {
            if (ownsConnection)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public static (string Schema, string Table) SplitEntityName(string entityName)
    {
        var trimmed = entityName.Trim();
        var parts = trimmed.Split('.', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            RequireIdentifier(parts[0], "table");
            return ("dbo", parts[0]);
        }

        RequireIdentifier(parts[0], "schema");
        RequireIdentifier(parts[1], "table");
        return (parts[0], parts[1]);
    }

    private static void RequireIdentifier(string value, string kind)
    {
        if (!Identifier.IsMatch(value))
        {
            throw new InvalidOperationException($"Sage {kind} '{value}' is not a valid identifier.");
        }
    }
}
