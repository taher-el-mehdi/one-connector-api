using System.Data;
using System.Globalization;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Domain.Entities;
using DocuWareSageConnector.Domain.Enums;
using DocuWareSageConnector.Domain.Mapping;
using DocuWareSageConnector.Infrastructure.Configuration;
using DocuWareSageConnector.Infrastructure.Persistence;
using DocuWareSageConnector.Sage.Queries;
using DocuWareSageConnector.Sage.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DocuWareSageConnector.Sage.Repositories;

public sealed class SageSqlEntityRepository : ISageEntityRepository
{
    private readonly ISageSqlConnectionFactory _connectionFactory;
    private readonly SageQueryDefinition _definition;
    private readonly SageOptions _options;

    public SageSqlEntityRepository(
        ISageSqlConnectionFactory connectionFactory,
        SageQueryDefinition definition,
        IOptions<SageOptions> options)
    {
        _connectionFactory = connectionFactory;
        _definition = definition;
        _options = options.Value;
        EntityType = definition.EntityType;
    }

    public EntityType EntityType { get; }

    public async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SageEntityRecord>> GetAllAsync(CancellationToken cancellationToken)
    {
        var sql = $"SELECT {SelectList()} FROM dbo.{_definition.TableName}";
        if (!string.IsNullOrWhiteSpace(_definition.FilterSql))
        {
            sql += $" WHERE {_definition.FilterSql}";
        }

        sql += $" ORDER BY {_definition.KeyColumn}";
        return await QueryAsync(sql, _definition.FilterParameters, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SageEntityRecord?> GetByKeyAsync(string key, CancellationToken cancellationToken)
    {
        var sql = $"SELECT {SelectList()} FROM dbo.{_definition.TableName} WHERE {_definition.KeyColumn} = @key";
        var rows = await QueryAsync(sql, new Dictionary<string, object> { ["key"] = key }, cancellationToken)
            .ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    public async Task UpdateAsync(
        string key,
        IReadOnlyDictionary<string, object?> writableColumns,
        int cbMarq,
        CancellationToken cancellationToken)
    {
        if (writableColumns.Count == 0)
        {
            return;
        }

        foreach (var column in writableColumns.Keys)
        {
            SageQueryCatalog.EnsureAllowedColumn(column);
        }

        SageQueryCatalog.EnsureAllowedColumn(_definition.KeyColumn);
        SageQueryCatalog.EnsureAllowedColumn("cbMarq");

        var assignments = writableColumns.Keys
            .Select((column, index) => $"{column} = @p{index}")
            .ToArray();

        var sql =
            $"UPDATE dbo.{_definition.TableName} SET {string.Join(", ", assignments)} " +
            $"WHERE {_definition.KeyColumn} = @key AND cbMarq = @cbMarq";

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        var index = 0;
        foreach (var pair in writableColumns)
        {
            command.Parameters.Add(CreateParameter($"@p{index}", pair.Value));
            index++;
        }

        command.Parameters.Add(CreateParameter("@key", key));
        command.Parameters.Add(CreateParameter("@cbMarq", cbMarq));

        var rows = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (rows == 0)
        {
            throw new InvalidOperationException(
                $"{key}: cbMarq changed (Sage 80011). Close Sage 100 and retry.");
        }
    }

    public async Task InsertAsync(
        IReadOnlyDictionary<string, object?> columns,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>(columns, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _definition.InsertDefaults)
        {
            payload.TryAdd(pair.Key, pair.Value);
        }

        payload.Remove("cbMarq");
        var now = DateTime.Now;
        payload["cbCreation"] = now;
        payload["cbModification"] = now;

        foreach (var column in payload.Keys)
        {
            SageQueryCatalog.EnsureAllowedColumn(column);
        }

        var names = payload.Keys.ToArray();
        var sql =
            $"INSERT INTO dbo.{_definition.TableName} ({string.Join(", ", names)}) " +
            $"VALUES ({string.Join(", ", names.Select((_, i) => "@p" + i))})";

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        for (var i = 0; i < names.Length; i++)
        {
            command.Parameters.Add(CreateParameter($"@p{i}", payload[names[i]]));
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private string SelectList() => string.Join(", ", _definition.SelectColumns);

    private async Task<IReadOnlyList<SageEntityRecord>> QueryAsync(
        string sql,
        IReadOnlyDictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        var results = new List<SageEntityRecord>();
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        if (parameters is not null)
        {
            foreach (var pair in parameters)
            {
                command.Parameters.Add(CreateParameter("@" + pair.Key, pair.Value));
            }
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var columns = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                columns[name] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            var key = EntityMapper.ReadSageKey(EntityType, columns)
                      ?? Convert.ToString(columns[_definition.KeyColumn], CultureInfo.InvariantCulture)
                      ?? string.Empty;

            int? cbMarq = null;
            if (columns.TryGetValue("cbMarq", out var marq) && marq is not null)
            {
                cbMarq = Convert.ToInt32(marq, CultureInfo.InvariantCulture);
            }

            results.Add(new SageEntityRecord
            {
                Key = key.Trim(),
                CbMarq = cbMarq,
                Columns = columns
            });
        }

        return results;
    }

    private static SqlParameter CreateParameter(string name, object? value)
    {
        return new SqlParameter(name, value ?? DBNull.Value)
        {
            Direction = ParameterDirection.Input
        };
    }
}

public sealed class SageRepositoryFactory : ISageRepositoryFactory
{
    private readonly Dictionary<EntityType, ISageEntityRepository> _repositories;
    private readonly SageMappingRelocation _mappingRelocation;

    public SageRepositoryFactory(
        ISageSqlConnectionFactory connectionFactory,
        SageQueryCatalog catalog,
        IOptions<SageOptions> options,
        SageMappingRelocation mappingRelocation)
    {
        _mappingRelocation = mappingRelocation;
        _repositories = Enum.GetValues<EntityType>().ToDictionary(
            type => type,
            type => (ISageEntityRepository)new SageSqlEntityRepository(connectionFactory, catalog.For(type), options));
    }

    public ISageEntityRepository Get(EntityType entityType) => _repositories[entityType];

    public async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        await _repositories[EntityType.Supplier].TestConnectionAsync(cancellationToken).ConfigureAwait(false);
        await _mappingRelocation.RemoveFromSageAsync(cancellationToken).ConfigureAwait(false);
    }
}
