using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Domain.Entities;
using DocuWareSageConnector.Domain.Enums;
using MySqlConnector;

namespace DocuWareSageConnector.Infrastructure.Persistence;

public sealed class MySqlSyncTrackingStore : ISyncTrackingStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MySqlSyncTrackingStore(IConfiguration configuration)
    {
        _connectionString = ConnectorStoreConnections.RequireConnectionString(configuration);
    }

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<SyncTrackingRecord?> GetAsync(
        SyncDirection direction,
        EntityType entityType,
        string sageNumber,
        CancellationToken cancellationToken) =>
        QuerySingleAsync(
            """
            SELECT id, id_synchronization, status,
                   created_at, updated_at, last_attempt_at, last_success_at, retry_count, error_message, file
            FROM logs
            WHERE CAST(JSON_UNQUOTE(JSON_EXTRACT(file, '$.direction')) AS SIGNED) = @direction
              AND CAST(JSON_UNQUOTE(JSON_EXTRACT(file, '$.entityType')) AS SIGNED) = @entityType
              AND JSON_UNQUOTE(JSON_EXTRACT(file, '$.sageNumber')) = @sageNumber
            LIMIT 1
            """,
            command =>
            {
                command.Parameters.AddWithValue("@direction", (int)direction);
                command.Parameters.AddWithValue("@entityType", (int)entityType);
                command.Parameters.AddWithValue("@sageNumber", sageNumber);
            },
            cancellationToken);

    public Task<SyncTrackingRecord?> GetForSynchronizationAsync(
        int synchronizationId,
        string sageNumber,
        CancellationToken cancellationToken) =>
        QuerySingleAsync(
            """
            SELECT id, id_synchronization, status,
                   created_at, updated_at, last_attempt_at, last_success_at, retry_count, error_message, file
            FROM logs
            WHERE id_synchronization = @synchronizationId
              AND JSON_UNQUOTE(JSON_EXTRACT(file, '$.sageNumber')) = @sageNumber
            ORDER BY updated_at DESC
            LIMIT 1
            """,
            command =>
            {
                command.Parameters.AddWithValue("@synchronizationId", synchronizationId);
                command.Parameters.AddWithValue("@sageNumber", sageNumber);
            },
            cancellationToken);

    public Task<SyncTrackingRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        QuerySingleAsync(
            """
            SELECT id, id_synchronization, status,
                   created_at, updated_at, last_attempt_at, last_success_at, retry_count, error_message, file
            FROM logs
            WHERE id = @id
            LIMIT 1
            """,
            command => command.Parameters.AddWithValue("@id", id.ToString("D")),
            cancellationToken);

    public Task<SyncTrackingRecord?> GetByDocumentIdAsync(
        SyncDirection direction,
        EntityType entityType,
        int documentId,
        CancellationToken cancellationToken) =>
        QuerySingleAsync(
            """
            SELECT id, id_synchronization, status,
                   created_at, updated_at, last_attempt_at, last_success_at, retry_count, error_message, file
            FROM logs
            WHERE CAST(JSON_UNQUOTE(JSON_EXTRACT(file, '$.direction')) AS SIGNED) = @direction
              AND CAST(JSON_UNQUOTE(JSON_EXTRACT(file, '$.entityType')) AS SIGNED) = @entityType
              AND CAST(JSON_UNQUOTE(JSON_EXTRACT(file, '$.docuWareDocumentId')) AS SIGNED) = @documentId
            LIMIT 1
            """,
            command =>
            {
                command.Parameters.AddWithValue("@direction", (int)direction);
                command.Parameters.AddWithValue("@entityType", (int)entityType);
                command.Parameters.AddWithValue("@documentId", documentId);
            },
            cancellationToken);

    public async Task UpsertAsync(SyncTrackingRecord record, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new MySqlCommand(UpsertSql, connection);
            Bind(command, record);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IReadOnlyList<SyncTrackingRecord>> ListAsync(
        SyncStatus? status,
        int take,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(take, 1, 500);
        var sql = status is null
            ? $"""
               SELECT id, id_synchronization, status,
                      created_at, updated_at, last_attempt_at, last_success_at, retry_count, error_message, file
               FROM logs
               ORDER BY updated_at DESC
               LIMIT {limit}
               """
            : $"""
               SELECT id, id_synchronization, status,
                      created_at, updated_at, last_attempt_at, last_success_at, retry_count, error_message, file
               FROM logs
               WHERE status = @status
               ORDER BY updated_at DESC
               LIMIT {limit}
               """;
        return QueryManyAsync(
            sql,
            command =>
            {
                if (status is not null)
                {
                    command.Parameters.AddWithValue("@status", (int)status.Value);
                }
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<SyncTrackingRecord>> ListErrorsAsync(int take, CancellationToken cancellationToken) =>
        ListAsync(SyncStatus.Failed, take, cancellationToken);

    private async Task<SyncTrackingRecord?> QuerySingleAsync(
        string sql,
        Action<MySqlCommand> bind,
        CancellationToken cancellationToken)
    {
        var rows = await QueryManyAsync(sql, bind, cancellationToken).ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    private async Task<IReadOnlyList<SyncTrackingRecord>> QueryManyAsync(
        string sql,
        Action<MySqlCommand> bind,
        CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(sql, connection);
        bind(command);
        var results = new List<SyncTrackingRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Read(reader));
        }

        return results;
    }

    private const string UpsertSql =
        """
        INSERT INTO logs (
            id, id_synchronization, status,
            created_at, updated_at, last_attempt_at, last_success_at, retry_count, error_message, file)
        VALUES (
            @id, @synchronizationId, @status,
            @createdAt, @updatedAt, @lastAttemptAt, @lastSuccessAt, @retryCount, @errorMessage, CAST(@file AS JSON))
        ON DUPLICATE KEY UPDATE
            id_synchronization = VALUES(id_synchronization),
            status = VALUES(status),
            updated_at = VALUES(updated_at),
            last_attempt_at = VALUES(last_attempt_at),
            last_success_at = VALUES(last_success_at),
            retry_count = VALUES(retry_count),
            error_message = VALUES(error_message),
            file = VALUES(file)
        """;

    private static void Bind(MySqlCommand command, SyncTrackingRecord record)
    {
        command.Parameters.AddWithValue("@id", record.Id.ToString("D"));
        command.Parameters.AddWithValue("@synchronizationId", (object?)record.SynchronizationId ?? DBNull.Value);
        command.Parameters.AddWithValue("@status", (int)record.Status);
        command.Parameters.AddWithValue("@createdAt", record.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("@updatedAt", record.UpdatedAt.UtcDateTime);
        command.Parameters.AddWithValue("@lastAttemptAt", record.LastAttemptAt is null ? DBNull.Value : record.LastAttemptAt.Value.UtcDateTime);
        command.Parameters.AddWithValue("@lastSuccessAt", record.LastSuccessAt is null ? DBNull.Value : record.LastSuccessAt.Value.UtcDateTime);
        command.Parameters.AddWithValue("@retryCount", record.RetryCount);
        command.Parameters.AddWithValue("@errorMessage", (object?)record.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("@file", (object?)record.SerializeFile() ?? DBNull.Value);
    }

    private static SyncTrackingRecord Read(MySqlDataReader reader)
    {
        var record = new SyncTrackingRecord
        {
            Id = Guid.Parse(reader.GetString("id")),
            SynchronizationId = reader.IsDBNull(reader.GetOrdinal("id_synchronization"))
                ? null
                : reader.GetInt32("id_synchronization"),
            Direction = SyncDirection.SageToDocuWare,
            EntityType = EntityType.Supplier,
            Status = (SyncStatus)reader.GetInt32("status"),
            CreatedAt = ReadUtc(reader, "created_at"),
            UpdatedAt = ReadUtc(reader, "updated_at"),
            LastAttemptAt = ReadUtcOrNull(reader, "last_attempt_at"),
            LastSuccessAt = ReadUtcOrNull(reader, "last_success_at"),
            RetryCount = reader.GetInt32("retry_count"),
            ErrorMessage = NullableText(reader, "error_message")
        };
        SyncTrackingRecord.ApplyFile(record, NullableText(reader, "file"));
        return record;
    }

    private static string? NullableText(MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset ReadUtc(MySqlDataReader reader, string column) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(column), DateTimeKind.Utc));

    private static DateTimeOffset? ReadUtcOrNull(MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal)
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
    }
}
