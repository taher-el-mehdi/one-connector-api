using System.Data;
using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DocuWareSageConnector.Sage.Sql;

public sealed class SageSchemaReader
{
    private readonly ISageSqlConnectionFactory _connections;
    private readonly SageOptions _options;

    public SageSchemaReader(ISageSqlConnectionFactory connections, IOptions<SageOptions> options)
    {
        _connections = connections;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SageTableDto>> ListTablesAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TABLE_SCHEMA, TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_SCHEMA, TABLE_NAME
            """;

        var tables = new List<SageTableDto>();
        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tables.Add(new SageTableDto
            {
                Schema = reader.GetString(0),
                Name = reader.GetString(1)
            });
        }

        return tables;
    }

    public async Task<SageTableDetailDto?> GetTableAsync(string schema, string name, CancellationToken cancellationToken)
    {
        const string existsSql = """
            SELECT 1
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
              AND TABLE_SCHEMA = @schema
              AND TABLE_NAME = @name
            """;
        const string columnsSql = """
            SELECT
                COLUMN_NAME,
                DATA_TYPE,
                CHARACTER_MAXIMUM_LENGTH,
                NUMERIC_PRECISION,
                NUMERIC_SCALE,
                IS_NULLABLE,
                COLUMN_DEFAULT
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @schema
              AND TABLE_NAME = @name
            ORDER BY ORDINAL_POSITION
            """;

        await using var connection = _connections.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var exists = connection.CreateCommand();
        exists.CommandText = existsSql;
        exists.CommandTimeout = _options.CommandTimeoutSeconds;
        AddName(exists, schema, name);
        var found = await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (found is null)
        {
            return null;
        }

        var fields = new List<SageColumnDto>();
        await using var command = connection.CreateCommand();
        command.CommandText = columnsSql;
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        AddName(command, schema, name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            fields.Add(new SageColumnDto
            {
                Name = reader.GetString(0),
                DataType = reader.GetString(1),
                MaxLength = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                Precision = reader.IsDBNull(3) ? null : Convert.ToInt32(reader.GetValue(3)),
                Scale = reader.IsDBNull(4) ? null : Convert.ToInt32(reader.GetValue(4)),
                Nullable = string.Equals(reader.GetString(5), "YES", StringComparison.OrdinalIgnoreCase),
                DefaultValue = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return new SageTableDetailDto
        {
            Schema = schema,
            Name = name,
            Fields = fields
        };
    }

    private static void AddName(SqlCommand command, string schema, string name)
    {
        command.Parameters.Add(new SqlParameter("@schema", SqlDbType.NVarChar, 128) { Value = schema });
        command.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 128) { Value = name });
    }
}
