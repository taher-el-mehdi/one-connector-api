using System.Text.RegularExpressions;
using DocuWareSageConnector.Infrastructure.Configuration;
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

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadAsync(
        string entityName,
        IReadOnlyList<string> columns,
        CancellationToken cancellationToken)
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
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = _options.CommandTimeoutSeconds;
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
