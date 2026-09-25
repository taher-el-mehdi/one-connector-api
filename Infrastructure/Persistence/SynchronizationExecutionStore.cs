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
                SELECT id, synchronization_id, status, started_at, completed_at,
                       total_records, processed_records, success_records, failed_records, skipped_records,
                       retry_count, error_message, created_at
                FROM synchronization_run
                WHERE synchronization_id = @synchronizationId
                ORDER BY started_at DESC, created_at DESC, id DESC
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
                SELECT id, synchronization_id, status, started_at, completed_at,
                       total_records, processed_records, success_records, failed_records, skipped_records,
                       retry_count, error_message, created_at
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
        var sourceId = Trim(record.SourceRecordId, 128);
        var success = string.Equals(record.Status, "success", StringComparison.OrdinalIgnoreCase);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(
                """
                MERGE synchronization_record AS target
                USING (SELECT @synchronizationId AS synchronization_id, @sourceRecordId AS source_record_id) AS source
                   ON target.synchronization_id = source.synchronization_id
                  AND target.source_record_id = source.source_record_id
                WHEN MATCHED THEN
                    UPDATE SET
                        last_run_id = @runId,
                        source_business_key = @businessKey,
                        source_hash = @sourceHash,
                        destination_record_id = @destinationRecordId,
                        status = @status,
                        attempt_count = target.attempt_count + 1,
                        last_attempt_at = @now,
                        last_success_at = CASE WHEN @success = 1 THEN @now ELSE target.last_success_at END,
                        error_message = @errorMessage,
                        updated_at = @now
                WHEN NOT MATCHED THEN
                    INSERT (
                        synchronization_id, last_run_id, source_record_id, source_business_key, source_hash,
                        destination_record_id, status, attempt_count, last_attempt_at, last_success_at,
                        error_message, created_at, updated_at)
                    VALUES (
                        @synchronizationId, @runId, @sourceRecordId, @businessKey, @sourceHash,
                        @destinationRecordId, @status, 1, @now, CASE WHEN @success = 1 THEN @now ELSE NULL END,
                        @errorMessage, @now, @now);
                """,
                connection);
            command.Parameters.AddWithValue("@synchronizationId", record.SynchronizationId);
            command.Parameters.AddWithValue("@runId", record.RunId);
            command.Parameters.AddWithValue("@sourceRecordId", sourceId);
            command.Parameters.AddWithValue("@businessKey", (object?)Trim(record.SourceBusinessKey, 256) ?? DBNull.Value);
            command.Parameters.AddWithValue("@sourceHash", (object?)Trim(record.SourceHash, 128) ?? DBNull.Value);
            command.Parameters.AddWithValue("@destinationRecordId", (object?)Trim(record.DestinationRecordId, 128) ?? DBNull.Value);
            command.Parameters.AddWithValue("@status", record.Status);
            command.Parameters.AddWithValue("@now", now);
            command.Parameters.AddWithValue("@success", success ? 1 : 0);
            command.Parameters.AddWithValue("@errorMessage", (object?)record.ErrorMessage ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    public async Task<IReadOnlySet<string>> ListInsertedSourceIdsAsync(
        int synchronizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(
                """
                SELECT source_record_id
                FROM synchronization_record
                WHERE synchronization_id = @synchronizationId
                  AND status = N'success'
                  AND destination_record_id IS NOT NULL
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
        string? status,
        Guid? lastRunId,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 0);
        pageSize = ClampPageSize(pageSize);
        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant();
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
            if (normalizedStatus is not null)
            {
                filters.Add("status = @status");
            }

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
                        if (normalizedStatus is not null)
                        {
                            command.Parameters.AddWithValue("@status", normalizedStatus);
                        }

                        if (lastRunId is Guid run)
                        {
                            command.Parameters.AddWithValue("@lastRunId", run);
                        }
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            await using var command = new SqlCommand(
                $"""
                SELECT id, synchronization_id, last_run_id, source_record_id, source_business_key, source_hash,
                       destination_record_id, status, attempt_count, last_attempt_at, last_success_at,
                       error_code, error_message, created_at, updated_at
                FROM synchronization_record
                WHERE {where}
                ORDER BY updated_at DESC, created_at DESC, id DESC
                OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY
                """,
                connection);
            command.Parameters.AddWithValue("@synchronizationId", synchronizationId);
            if (normalizedStatus is not null)
            {
                command.Parameters.AddWithValue("@status", normalizedStatus);
            }

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
            StartedAt = ToOffset(ReadUtc(reader, "started_at")),
            CompletedAt = ToOffset(ReadUtcOrNull(reader, "completed_at")),
            TotalRecords = reader.GetInt32(reader.GetOrdinal("total_records")),
            ProcessedRecords = reader.GetInt32(reader.GetOrdinal("processed_records")),
            SuccessRecords = reader.GetInt32(reader.GetOrdinal("success_records")),
            FailedRecords = reader.GetInt32(reader.GetOrdinal("failed_records")),
            SkippedRecords = reader.GetInt32(reader.GetOrdinal("skipped_records")),
            RetryCount = reader.GetInt32(reader.GetOrdinal("retry_count")),
            ErrorMessage = ReadStringOrNull(reader, "error_message"),
            CreatedAt = ToOffset(ReadUtc(reader, "created_at"))
        };

    private static SynchronizationSourceRecordDto ReadRecord(SqlDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(reader.GetOrdinal("id")),
            SynchronizationId = reader.GetInt32(reader.GetOrdinal("synchronization_id")),
            LastRunId = reader.GetGuid(reader.GetOrdinal("last_run_id")),
            SourceRecordId = reader.GetString(reader.GetOrdinal("source_record_id")),
            SourceBusinessKey = ReadStringOrNull(reader, "source_business_key"),
            SourceHash = ReadStringOrNull(reader, "source_hash"),
            DestinationRecordId = ReadStringOrNull(reader, "destination_record_id"),
            Status = reader.GetString(reader.GetOrdinal("status")),
            AttemptCount = reader.GetInt32(reader.GetOrdinal("attempt_count")),
            LastAttemptAt = ToOffset(ReadUtcOrNull(reader, "last_attempt_at")),
            LastSuccessAt = ToOffset(ReadUtcOrNull(reader, "last_success_at")),
            ErrorCode = ReadStringOrNull(reader, "error_code"),
            ErrorMessage = ReadStringOrNull(reader, "error_message"),
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
