using DocuWareSageConnector.Infrastructure.Configuration;
using DocuWareSageConnector.Sage.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace DocuWareSageConnector.Infrastructure.Persistence;

public sealed class SageMappingRelocation
{
    private const string UnassignedUser = "00000000-0000-0000-0000-000000000000";

    private readonly ISageSqlConnectionFactory _sageConnections;
    private readonly SageOptions _options;
    private readonly string _connectionString;
    private readonly ILogger<SageMappingRelocation> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SageMappingRelocation(
        ISageSqlConnectionFactory sageConnections,
        IOptions<SageOptions> options,
        IConfiguration configuration,
        ILogger<SageMappingRelocation> logger)
    {
        _sageConnections = sageConnections;
        _options = options.Value;
        _connectionString = ConnectorStoreConnections.RequireConnectionString(configuration);
        _logger = logger;
    }

    public async Task RemoveFromSageAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Server) || string.IsNullOrWhiteSpace(_options.Database))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MoveAndDropAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not remove mapping_table and mapping_field from Sage database {Database}.",
                _options.Database);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task MoveAndDropAsync(CancellationToken cancellationToken)
    {
        await using var sage = _sageConnections.Create();
        sage.Open();
        var tableId = await ScalarAsync(sage, "SELECT OBJECT_ID(N'dbo.mapping_table', N'U')", cancellationToken)
            .ConfigureAwait(false);
        var fieldId = await ScalarAsync(sage, "SELECT OBJECT_ID(N'dbo.mapping_field', N'U')", cancellationToken)
            .ConfigureAwait(false);
        if (tableId is null && fieldId is null)
        {
            return;
        }

        var copied = 0;
        if (tableId is not null)
        {
            copied = await CopyAsync(sage, fieldId is not null, cancellationToken).ConfigureAwait(false);
        }

        if (fieldId is not null)
        {
            await ExecuteAsync(sage, "DROP TABLE dbo.mapping_field", cancellationToken).ConfigureAwait(false);
        }

        if (tableId is not null)
        {
            await ExecuteAsync(sage, "DROP TABLE dbo.mapping_table", cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Removed mapping_table and mapping_field from Sage database {Database} after copying {Count} mappings into MySQL.",
            _options.Database,
            copied);
    }

    private async Task<int> CopyAsync(SqlConnection sage, bool includeFields, CancellationToken cancellationToken)
    {
        var mappings = new List<SageMappingRow>();
        await using (var command = sage.CreateCommand())
        {
            command.CommandText = "SELECT id, entity_name, cabinet_name FROM dbo.mapping_table";
            command.CommandTimeout = _options.CommandTimeoutSeconds;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                mappings.Add(new SageMappingRow(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        var fields = new List<SageFieldRow>();
        if (includeFields)
        {
            await using var command = sage.CreateCommand();
            command.CommandText = """
                SELECT id_mapping_table, entity_field_name, cabinet_field_name,
                       entity_type_name, cabinet_type_name, entity_type_long, cabinet_type_long
                FROM dbo.mapping_field
                """;
            command.CommandTimeout = _options.CommandTimeoutSeconds;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                fields.Add(new SageFieldRow(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6)));
            }
        }

        await using var mysql = new MySqlConnection(_connectionString);
        await mysql.OpenAsync(cancellationToken).ConfigureAwait(false);
        var company = await ConnectorCompany.RequireAsync(mysql, cancellationToken).ConfigureAwait(false);
        var actor = await FirstUserAsync(mysql, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        await using var transaction = await mysql.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var mapping in mappings)
        {
            var id = await InsertMappingAsync(mysql, transaction, company, mapping, actor, now, cancellationToken)
                .ConfigureAwait(false);
            foreach (var field in fields.Where(item => item.MappingId == mapping.Id))
            {
                await InsertFieldAsync(mysql, transaction, company, id, field, actor, now, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return mappings.Count;
    }

    private static async Task<int> InsertMappingAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string company,
        SageMappingRow mapping,
        string actor,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var existing = new MySqlCommand(
            """
            SELECT id
            FROM mapping_table
            WHERE company = @company AND entity_name = @entityName AND cabinet_name = @cabinetName
            LIMIT 1
            """,
            connection,
            transaction);
        existing.Parameters.AddWithValue("@company", company);
        existing.Parameters.AddWithValue("@entityName", mapping.EntityName);
        existing.Parameters.AddWithValue("@cabinetName", mapping.CabinetName);
        var found = await existing.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (found is not null and not DBNull)
        {
            return Convert.ToInt32(found);
        }

        await using var command = new MySqlCommand(
            """
            INSERT INTO mapping_table
                (company, entity_name, cabinet_name, created_by, created_at, updated_at, updated_by)
            VALUES
                (@company, @entityName, @cabinetName, @actor, @now, @now, @actor)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("@company", company);
        command.Parameters.AddWithValue("@entityName", mapping.EntityName);
        command.Parameters.AddWithValue("@cabinetName", mapping.CabinetName);
        command.Parameters.AddWithValue("@actor", actor);
        command.Parameters.AddWithValue("@now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(command.LastInsertedId);
    }

    private static async Task InsertFieldAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string company,
        int mappingId,
        SageFieldRow field,
        string actor,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var existing = new MySqlCommand(
            """
            SELECT id
            FROM mapping_field
            WHERE id_mapping_table = @mappingId AND entity_field_name = @entityFieldName
            LIMIT 1
            """,
            connection,
            transaction);
        existing.Parameters.AddWithValue("@mappingId", mappingId);
        existing.Parameters.AddWithValue("@entityFieldName", field.EntityFieldName);
        var found = await existing.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (found is not null and not DBNull)
        {
            await using var update = new MySqlCommand(
                """
                UPDATE mapping_field
                SET cabinet_field_name = @cabinetFieldName,
                    entity_type_name = @entityTypeName,
                    cabinet_type_name = @cabinetTypeName,
                    entity_type_long = @entityTypeLong,
                    cabinet_type_long = @cabinetTypeLong,
                    updated_at = @now,
                    updated_by = @actor
                WHERE id = @id
                """,
                connection,
                transaction);
            update.Parameters.AddWithValue("@cabinetFieldName", field.CabinetFieldName);
            update.Parameters.AddWithValue("@entityTypeName", (object?)field.EntityTypeName ?? DBNull.Value);
            update.Parameters.AddWithValue("@cabinetTypeName", (object?)field.CabinetTypeName ?? DBNull.Value);
            update.Parameters.AddWithValue("@entityTypeLong", (object?)field.EntityTypeLong ?? DBNull.Value);
            update.Parameters.AddWithValue("@cabinetTypeLong", (object?)field.CabinetTypeLong ?? DBNull.Value);
            update.Parameters.AddWithValue("@now", now);
            update.Parameters.AddWithValue("@actor", actor);
            update.Parameters.AddWithValue("@id", Convert.ToInt32(found));
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var command = new MySqlCommand(
            """
            INSERT INTO mapping_field
                (company, id_mapping_table, entity_field_name, cabinet_field_name, entity_type_name, cabinet_type_name,
                 entity_type_long, cabinet_type_long, created_by, created_at, updated_at, updated_by)
            VALUES
                (@company, @mappingId, @entityFieldName, @cabinetFieldName, @entityTypeName, @cabinetTypeName,
                 @entityTypeLong, @cabinetTypeLong, @actor, @now, @now, @actor)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("@company", company);
        command.Parameters.AddWithValue("@mappingId", mappingId);
        command.Parameters.AddWithValue("@entityFieldName", field.EntityFieldName);
        command.Parameters.AddWithValue("@cabinetFieldName", field.CabinetFieldName);
        command.Parameters.AddWithValue("@entityTypeName", (object?)field.EntityTypeName ?? DBNull.Value);
        command.Parameters.AddWithValue("@cabinetTypeName", (object?)field.CabinetTypeName ?? DBNull.Value);
        command.Parameters.AddWithValue("@entityTypeLong", (object?)field.EntityTypeLong ?? DBNull.Value);
        command.Parameters.AddWithValue("@cabinetTypeLong", (object?)field.CabinetTypeLong ?? DBNull.Value);
        command.Parameters.AddWithValue("@actor", actor);
        command.Parameters.AddWithValue("@now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> FirstUserAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(
            "SELECT id FROM `user` WHERE is_active = 1 ORDER BY created_at LIMIT 1",
            connection);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var id = value is null or DBNull ? null : Convert.ToString(value);
        return string.IsNullOrEmpty(id) ? UnassignedUser : id;
    }

    private async Task<object?> ScalarAsync(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? null : value;
    }

    private async Task ExecuteAsync(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record SageMappingRow(int Id, string EntityName, string CabinetName);

    private sealed record SageFieldRow(
        int MappingId,
        string EntityFieldName,
        string CabinetFieldName,
        string? EntityTypeName,
        string? CabinetTypeName,
        int? EntityTypeLong,
        int? CabinetTypeLong);
}

public sealed class SageMappingRelocationService : BackgroundService
{
    private readonly SageMappingRelocation _relocation;

    public SageMappingRelocationService(SageMappingRelocation relocation)
    {
        _relocation = relocation;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        _relocation.RemoveFromSageAsync(stoppingToken);
}
