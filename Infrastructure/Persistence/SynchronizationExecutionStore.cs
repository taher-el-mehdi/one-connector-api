using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DocuWareSageConnector.Infrastructure.Persistence;

public sealed class SynchronizationExecutionStore : ISynchronizationExecutionStore
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly string _connectionString;

    public SynchronizationExecutionStore(IConfiguration configuration)
    {
        _connectionString = ConnectorStoreConnections.RequireConnectionString(configuration);
    }

    public async Task<SynchronizationRunPageDto?> ListRunsAsync(
        int synchronizationId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 0);
        pageSize = ClampPageSize(pageSize);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            if (!await SynchronizationExistsAsync(connection, synchronizationId, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            var total = await CountAsync(
                    connection,
                    "SELECT COUNT(*) FROM synchronization_run WHERE synchronization_id = @synchronizationId",
                    command => command.Parameters.AddWithValue("@synchronizationId", synchronizationId),
                    cancellationToken)
                .ConfigureAwait(false);

            await using var command = new SqlCommand(
                """
                SELECT id, synchronization_id, status, start_at, completed_at,
                       records_inserted, records_updated, error_message, created_at
                FROM synchronization_run
                WHERE synchronization_id = @synchronizationId
                ORDER BY start_at DESC, created_at DESC, id DESC
                OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY
                """,
                connection);
            command.Parameters.AddWithValue("@synchronizationId", synchronizationId);
            command.Parameters.AddWithValue("@offset", page * pageSize);
            command.Parameters.AddWithValue("@pageSize", pageSize);
            var items = new List<SynchronizationRunDto>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(ReadRun(reader));
            }

            return new SynchronizationRunPageDto
            {
                Items = items,
                Total = total,
                Page = page,
                PageSize = pageSize
            };
        }
        catch (SqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    public async Task<SynchronizationRunDto?> GetRunAsync(
        int synchronizationId,
        Guid runId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            if (!await SynchronizationExistsAsync(connection, synchronizationId, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            await using var command = new SqlCommand(
                """
                SELECT id, synchronization_id, status, start_at, completed_at,
                       records_inserted, records_updated, error_message, created_at
                FROM synchronization_run
                WHERE synchronization_id = @synchronizationId AND id = @runId
                """,
                connection);
            command.Parameters.AddWithValue("@synchronizationId", synchronizationId);
            command.Parameters.AddWithValue("@runId", runId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadRun(reader) : null;
        }
        catch (SqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    public async Task UpsertRecordAsync(SynchronizationRecordWrite record, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var entityId = Trim(record.EntityId, 512);
        if (string.IsNullOrWhiteSpace(entityId))
        {
            throw new ArgumentException("Entity id is required.");
        }

        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(
                """
                MERGE synchronization_record AS target
                USING (SELECT @synchronizationId AS synchronization_id, @entityId AS entity_id) AS source
                   ON target.synchronization_id = source.synchronization_id
                  AND target.entity_id = source.entity_id
                WHEN MATCHED THEN
                    UPDATE SET
                        last_run_id = @runId,
                        docuware_id = @docuwareId,
                        error = @error,
                        updated_at = @now
                WHEN NOT MATCHED THEN
                    INSERT (
                        synchronization_id, last_run_id, entity_id, docuware_id, error, created_at, updated_at)
                    VALUES (
                        @synchronizationId, @runId, @entityId, @docuwareId, @error, @now, @now);
                """,
                connection);
            command.Parameters.AddWithValue("@synchronizationId", record.SynchronizationId);
            command.Parameters.AddWithValue("@runId", record.RunId);
            command.Parameters.AddWithValue("@entityId", entityId);
            command.Parameters.AddWithValue("@docuwareId", (object?)Trim(record.DocuWareId, 128) ?? DBNull.Value);
            command.Parameters.AddWithValue("@error", (object?)record.Error ?? DBNull.Value);
            command.Parameters.AddWithValue("@now", now);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    public async Task<IReadOnlySet<string>> ListInsertedEntityIdsAsync(
        int synchronizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(
                """
                SELECT entity_id
                FROM synchronization_record
                WHERE synchronization_id = @synchronizationId
                  AND docuware_id IS NOT NULL
                """,
                connection);
            command.Parameters.AddWithValue("@synchronizationId", synchronizationId);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                ids.Add(reader.GetString(0));
            }

            return ids;
        }
        catch (SqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    public async Task<SynchronizationSourceRecordPageDto?> ListRecordsAsync(
        int synchronizationId,
        int page,
        int pageSize,
        Guid? lastRunId,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 0);
        pageSize = ClampPageSize(pageSize);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            if (!await SynchronizationExistsAsync(connection, synchronizationId, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            if (lastRunId is Guid runId
                && !await RunExistsAsync(connection, synchronizationId, runId, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            var filters = new List<string> { "synchronization_id = @synchronizationId" };
            if (lastRunId is not null)
            {
                filters.Add("last_run_id = @lastRunId");
            }

            var where = string.Join(" AND ", filters);
            var total = await CountAsync(
                    connection,
                    $"SELECT COUNT(*) FROM synchronization_record WHERE {where}",
                    command =>
                    {
                        command.Parameters.AddWithValue("@synchronizationId", synchronizationId);
                        if (lastRunId is Guid run)
                        {
                            command.Parameters.AddWithValue("@lastRunId", run);
                        }
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            await using var command = new SqlCommand(
                $"""
                SELECT id, synchronization_id, last_run_id, entity_id, docuware_id, error, created_at, updated_at
                FROM synchronization_record
                WHERE {where}
                ORDER BY updated_at DESC, created_at DESC, id DESC
                OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY
                """,
                connection);
            command.Parameters.AddWithValue("@synchronizationId", synchronizationId);
            if (lastRunId is Guid runFilter)
            {
                command.Parameters.AddWithValue("@lastRunId", runFilter);
            }

            command.Parameters.AddWithValue("@offset", page * pageSize);
            command.Parameters.AddWithValue("@pageSize", pageSize);
            var items = new List<SynchronizationSourceRecordDto>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(ReadRecord(reader));
            }

            return new SynchronizationSourceRecordPageDto
            {
                Items = items,
                Total = total,
                Page = page,
                PageSize = pageSize
            };
        }
        catch (SqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private static int ClampPageSize(int pageSize) =>
        pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

    private static async Task<bool> SynchronizationExistsAsync(
        SqlConnection connection,
        int synchronizationId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM synchronization WHERE id = @id",
            connection);
        command.Parameters.AddWithValue("@id", synchronizationId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) > 0;
    }

    private static async Task<bool> RunExistsAsync(
        SqlConnection connection,
        int synchronizationId,
        Guid runId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM synchronization_run
            WHERE synchronization_id = @synchronizationId AND id = @runId
            """,
            connection);
        command.Parameters.AddWithValue("@synchronizationId", synchronizationId);
        command.Parameters.AddWithValue("@runId", runId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) > 0;
    }

    private static async Task<int> CountAsync(
        SqlConnection connection,
        string sql,
        Action<SqlCommand> bind,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection);
        bind(command);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static SynchronizationRunDto ReadRun(SqlDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(reader.GetOrdinal("id")),
            SynchronizationId = reader.GetInt32(reader.GetOrdinal("synchronization_id")),
            Status = reader.GetString(reader.GetOrdinal("status")),
            StartAt = ToOffset(ReadUtc(reader, "start_at")),
            CompletedAt = ToOffset(ReadUtcOrNull(reader, "completed_at")),
            RecordsInserted = reader.GetInt32(reader.GetOrdinal("records_inserted")),
            RecordsUpdated = reader.GetInt32(reader.GetOrdinal("records_updated")),
            ErrorMessage = ReadStringOrNull(reader, "error_message"),
            CreatedAt = ToOffset(ReadUtc(reader, "created_at"))
        };

    private static SynchronizationSourceRecordDto ReadRecord(SqlDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(reader.GetOrdinal("id")),
            SynchronizationId = reader.GetInt32(reader.GetOrdinal("synchronization_id")),
            LastRunId = reader.GetGuid(reader.GetOrdinal("last_run_id")),
            EntityId = reader.GetString(reader.GetOrdinal("entity_id")),
            DocuWareId = ReadStringOrNull(reader, "docuware_id"),
            Error = ReadStringOrNull(reader, "error"),
            CreatedAt = ToOffset(ReadUtc(reader, "created_at")),
            UpdatedAt = ToOffset(ReadUtc(reader, "updated_at"))
        };

    private static string? ReadStringOrNull(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTime ReadUtc(SqlDataReader reader, string column) =>
        DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal(column)), DateTimeKind.Utc);

    private static DateTime? ReadUtcOrNull(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal)
            ? null
            : DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);
    }

    private static DateTimeOffset ToOffset(DateTime value) => new(value);

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value is null ? null : new DateTimeOffset(value.Value);

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
