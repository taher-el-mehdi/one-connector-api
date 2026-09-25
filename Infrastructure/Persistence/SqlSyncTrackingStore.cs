using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Domain.Entities;
using DocuWareSageConnector.Domain.Enums;
using Microsoft.Data.SqlClient;

namespace DocuWareSageConnector.Infrastructure.Persistence;

public sealed class SqlSyncTrackingStore : ISyncTrackingStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SqlSyncTrackingStore(IConfiguration configuration)
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
            SELECT TOP (1) id, id_synchronization, status,
                   created_at, updated_at, last_attempt_at, last_success_at, retry_count, error_message, [file]
            FROM logs
            WHERE CAST(JSON_VALUE([file], '$.direction') AS int) = @direction
              AND CAST(JSON_VALUE([file], '$.entityType') AS int) = @entityType
              AND JSON_VALUE([file], '$.sageNumber') = @sageNumber
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
            SELECT TOP (1) id, id_synchronization, status,
                   created_at, updated_at, last_attempt_at, last_success_at, retry_count, error_message, [file]
            FROM logs
            WHERE id_synchronization = @synchronizationId
              AND JSON_VALUE([file], '$.sageNumber') = @sageNumber
            ORDER BY updated_at DESC
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
            SELECT TOP (1) id, id_synchronization, status,
                   created_at, updated_at, last_attempt_at, last_success_at, retry_count, error_message, [file]
            FROM logs
            WHERE id = @id
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
            SELECT TOP (1) id, id_synchronization, status,
                   created_at, updated_at, last_attempt_at, last_success_at, retry_count, error_message, [file]
            FROM logs
            WHERE CAST(JSON_VALUE([file], '$.direction') AS int) = @direction
              AND CAST(JSON_VALUE([file], '$.entityType') AS int) = @entityType
              AND CAST(JSON_VALUE([file], '$.docuWareDocumentId') AS int) = @documentId
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
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(UpsertSql, connection);
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
        // [file] must be bracketed: FILE is reserved in T-SQL.
        var sql = status is null
            ? """
              SELECT TOP (@take) id, id_synchronization, status,
                     created_at, updated_at, last_attempt_at, last_success_at, retry_count, error_message, [file]
              FROM logs
              ORDER BY updated_at DESC
              """
            : """
              SELECT TOP (@take) id, id_synchronization, status,
                     created_at, updated_at, last_attempt_at, last_success_at, retry_count, error_message, [file]
              FROM logs
              WHERE status = @status
              ORDER BY updated_at DESC
              """;
        return QueryManyAsync(
            sql,
            command =>
            {
                command.Parameters.AddWithValue("@take", limit);
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
        Action<SqlCommand> bind,
        CancellationToken cancellationToken)
    {
        var rows = await QueryManyAsync(sql, bind, cancellationToken).ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    private async Task<IReadOnlyList<SyncTrackingRecord>> QueryManyAsync(
        string sql,
        Action<SqlCommand> bind,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
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
        MERGE logs AS target
        USING (SELECT @id AS id) AS source
        ON target.id = source.id
        WHEN MATCHED THEN UPDATE SET
            id_synchronization = @synchronizationId,
            status = @status,
            updated_at = @updatedAt,
            last_attempt_at = @lastAttemptAt,
            last_success_at = @lastSuccessAt,
            retry_count = @retryCount,
            error_message = @errorMessage,
            [file] = @file
        WHEN NOT MATCHED THEN INSERT (
            id, id_synchronization, status,
            created_at, updated_at, last_attempt_at, last_success_at, retry_count, error_message, [file])
        VALUES (
            @id, @synchronizationId, @status,
            @createdAt, @updatedAt, @lastAttemptAt, @lastSuccessAt, @retryCount, @errorMessage, @file);
        """;

    private static void Bind(SqlCommand command, SyncTrackingRecord record)
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

    private static SyncTrackingRecord Read(SqlDataReader reader)
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

    private static string? NullableText(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset ReadUtc(SqlDataReader reader, string column) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(column), DateTimeKind.Utc));

    private static DateTimeOffset? ReadUtcOrNull(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal)
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
    }
}
