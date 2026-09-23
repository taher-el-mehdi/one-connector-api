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
            SELECT id, company, direction, entity_type, sage_number, docuware_document_id, status,
                   created_at, updated_at, last_attempt_at, last_success_at, retry_count, last_error, fingerprint
            FROM logs
            WHERE company = @company AND direction = @direction AND entity_type = @entityType AND sage_number = @sageNumber
            LIMIT 1
            """,
            command =>
            {
                command.Parameters.AddWithValue("@direction", (int)direction);
                command.Parameters.AddWithValue("@entityType", (int)entityType);
                command.Parameters.AddWithValue("@sageNumber", sageNumber);
            },
            cancellationToken);

    public Task<SyncTrackingRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        QuerySingleAsync(
            """
            SELECT id, company, direction, entity_type, sage_number, docuware_document_id, status,
                   created_at, updated_at, last_attempt_at, last_success_at, retry_count, last_error, fingerprint
            FROM logs
            WHERE company = @company AND id = @id
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
            SELECT id, company, direction, entity_type, sage_number, docuware_document_id, status,
                   created_at, updated_at, last_attempt_at, last_success_at, retry_count, last_error, fingerprint
            FROM logs
            WHERE company = @company AND direction = @direction AND entity_type = @entityType
              AND docuware_document_id = @documentId
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
               SELECT id, company, direction, entity_type, sage_number, docuware_document_id, status,
                      created_at, updated_at, last_attempt_at, last_success_at, retry_count, last_error, fingerprint
               FROM logs
               WHERE company = @company
               ORDER BY updated_at DESC
               LIMIT {limit}
               """
            : $"""
               SELECT id, company, direction, entity_type, sage_number, docuware_document_id, status,
                      created_at, updated_at, last_attempt_at, last_success_at, retry_count, last_error, fingerprint
               FROM logs
               WHERE company = @company AND status = @status
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
        command.Parameters.AddWithValue("@company", ConnectorCompany.Name);
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
            id, company, direction, entity_type, sage_number, docuware_document_id, status,
            created_at, updated_at, last_attempt_at, last_success_at, retry_count, last_error, fingerprint)
        VALUES (
            @id, @company, @direction, @entityType, @sageNumber, @documentId, @status,
            @createdAt, @updatedAt, @lastAttemptAt, @lastSuccessAt, @retryCount, @lastError, @fingerprint)
        ON DUPLICATE KEY UPDATE
            sage_number = VALUES(sage_number),
            docuware_document_id = VALUES(docuware_document_id),
            status = VALUES(status),
            updated_at = VALUES(updated_at),
            last_attempt_at = VALUES(last_attempt_at),
            last_success_at = VALUES(last_success_at),
            retry_count = VALUES(retry_count),
            last_error = VALUES(last_error),
            fingerprint = VALUES(fingerprint)
        """;

    private static void Bind(MySqlCommand command, SyncTrackingRecord record)
    {
        command.Parameters.AddWithValue("@id", record.Id.ToString("D"));
        command.Parameters.AddWithValue("@company", ConnectorCompany.Name);
        command.Parameters.AddWithValue("@direction", (int)record.Direction);
        command.Parameters.AddWithValue("@entityType", (int)record.EntityType);
        command.Parameters.AddWithValue("@sageNumber", (object?)record.SageNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("@documentId", (object?)record.DocuWareDocumentId ?? DBNull.Value);
        command.Parameters.AddWithValue("@status", (int)record.Status);
        command.Parameters.AddWithValue("@createdAt", record.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("@updatedAt", record.UpdatedAt.UtcDateTime);
        command.Parameters.AddWithValue("@lastAttemptAt", record.LastAttemptAt is null ? DBNull.Value : record.LastAttemptAt.Value.UtcDateTime);
        command.Parameters.AddWithValue("@lastSuccessAt", record.LastSuccessAt is null ? DBNull.Value : record.LastSuccessAt.Value.UtcDateTime);
        command.Parameters.AddWithValue("@retryCount", record.RetryCount);
        command.Parameters.AddWithValue("@lastError", (object?)record.LastError ?? DBNull.Value);
        command.Parameters.AddWithValue("@fingerprint", (object?)record.Fingerprint ?? DBNull.Value);
    }

    private static SyncTrackingRecord Read(MySqlDataReader reader) =>
        new()
        {
            Id = Guid.Parse(reader.GetString("id")),
            Direction = (SyncDirection)reader.GetInt32("direction"),
            EntityType = (EntityType)reader.GetInt32("entity_type"),
            SageNumber = NullableText(reader, "sage_number"),
            DocuWareDocumentId = reader.IsDBNull(reader.GetOrdinal("docuware_document_id"))
                ? null
                : reader.GetInt32("docuware_document_id"),
            Status = (SyncStatus)reader.GetInt32("status"),
            CreatedAt = ReadUtc(reader, "created_at"),
            UpdatedAt = ReadUtc(reader, "updated_at"),
            LastAttemptAt = ReadUtcOrNull(reader, "last_attempt_at"),
            LastSuccessAt = ReadUtcOrNull(reader, "last_success_at"),
            RetryCount = reader.GetInt32("retry_count"),
            LastError = NullableText(reader, "last_error"),
            Fingerprint = NullableText(reader, "fingerprint")
        };

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
