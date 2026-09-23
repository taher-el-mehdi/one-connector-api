using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Application.Synchronization;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace DocuWareSageConnector.Infrastructure.Persistence;

public sealed class SynchronizationStore : ISynchronizationStore
{
    private const string SelectColumns =
        """
        id, direction, source, destination, id_mapping_table, code, description, status,
        max_retries, timeout_seconds, next_run_at, retry_count, created_at, updated_at
        """;

    private readonly string _connectionString;

    public SynchronizationStore(IConfiguration configuration)
    {
        _connectionString = ConnectorStoreConnections.RequireConnectionString(configuration);
    }

    public async Task<SynchronizationListDto> ListAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            var company = await ConnectorCompany.RequireAsync(connection, cancellationToken).ConfigureAwait(false);
            await using var command = new MySqlCommand(
                $"""
                SELECT {SelectColumns}
                FROM synchronization
                WHERE company = @company
                ORDER BY id
                """,
                connection);
            command.Parameters.AddWithValue("@company", company);
            var rows = new List<SynchronizationRecordDto>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(ReadRow(reader));
            }

            return new SynchronizationListDto { Synchronizations = rows };
        }
        catch (MySqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    public async Task<SynchronizationRecordDto?> GetAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            var company = await ConnectorCompany.RequireAsync(connection, cancellationToken).ConfigureAwait(false);
            return await ReadAsync(connection, company, id, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    public async Task<SynchronizationRecordDto> CreateAsync(
        SaveSynchronizationRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var values = Normalize(request);
        var now = DateTime.UtcNow;
        var actor = userId.ToString("D");
        var nextRunAt = values.NextRunAt ?? now;
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            var company = await ConnectorCompany.RequireAsync(connection, cancellationToken).ConfigureAwait(false);
            await EnsureMappingAsync(connection, company, values.MappingTableId, cancellationToken).ConfigureAwait(false);
            await using var command = new MySqlCommand(
                """
                INSERT INTO synchronization
                    (company, direction, source, destination, id_mapping_table, code, description, status,
                     max_retries, timeout_seconds, next_run_at, retry_count, created_by, created_at, updated_at, updated_by)
                VALUES
                    (@company, @direction, @source, @destination, @mappingTableId, @code, @description, NULL,
                     @maxRetries, @timeoutSeconds, @nextRunAt, 0, @actor, @now, @now, @actor)
                """,
                connection);
            Bind(command, company, values, actor, now, nextRunAt);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var id = Convert.ToInt32(command.LastInsertedId);
            return await ReadAsync(connection, company, id, cancellationToken).ConfigureAwait(false)
                   ?? throw new SynchronizationStoreException(
                       "The synchronization was saved but could not be read back.",
                       new InvalidOperationException());
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            throw new ArgumentException("A synchronization with this code already exists.");
        }
        catch (MySqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    public async Task<SynchronizationRecordDto?> UpdateAsync(
        int id,
        SaveSynchronizationRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var values = Normalize(request);
        var now = DateTime.UtcNow;
        var actor = userId.ToString("D");
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            var company = await ConnectorCompany.RequireAsync(connection, cancellationToken).ConfigureAwait(false);
            if (await ReadAsync(connection, company, id, cancellationToken).ConfigureAwait(false) is null)
            {
                return null;
            }

            await EnsureMappingAsync(connection, company, values.MappingTableId, cancellationToken).ConfigureAwait(false);
            await using var command = new MySqlCommand(
                """
                UPDATE synchronization
                SET direction = @direction,
                    source = @source,
                    destination = @destination,
                    id_mapping_table = @mappingTableId,
                    code = @code,
                    description = @description,
                    max_retries = @maxRetries,
                    timeout_seconds = @timeoutSeconds,
                    next_run_at = @nextRunAt,
                    updated_at = @now,
                    updated_by = @actor
                WHERE id = @id AND company = @company
                """,
                connection);
            Bind(command, company, values, actor, now, values.NextRunAt);
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return await ReadAsync(connection, company, id, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            throw new ArgumentException("A synchronization with this code already exists.");
        }
        catch (MySqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            var company = await ConnectorCompany.RequireAsync(connection, cancellationToken).ConfigureAwait(false);
            await using var command = new MySqlCommand(
                "DELETE FROM synchronization WHERE id = @id AND company = @company",
                connection);
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@company", company);
            var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return affected > 0;
        }
        catch (MySqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    public async Task<SynchronizationRecordDto?> QueueNowAsync(int id, Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            var company = await ConnectorCompany.RequireAsync(connection, cancellationToken).ConfigureAwait(false);
            await using var command = new MySqlCommand(
                """
                UPDATE synchronization
                SET next_run_at = @now,
                    retry_count = 0,
                    updated_at = @now,
                    updated_by = @actor
                WHERE id = @id AND company = @company
                """,
                connection);
            command.Parameters.AddWithValue("@now", now);
            command.Parameters.AddWithValue("@actor", userId.ToString("D"));
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@company", company);
            var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected == 0)
            {
                return null;
            }

            return await ReadAsync(connection, company, id, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    public async Task<IReadOnlyList<int>> ListDueIdsAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            var company = await ConnectorCompany.RequireAsync(connection, cancellationToken).ConfigureAwait(false);
            await using var command = new MySqlCommand(
                """
                SELECT id
                FROM synchronization
                WHERE company = @company
                  AND next_run_at IS NOT NULL
                  AND next_run_at <= @now
                ORDER BY next_run_at, id
                """,
                connection);
            command.Parameters.AddWithValue("@company", company);
            command.Parameters.AddWithValue("@now", utcNow);
            var ids = new List<int>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                ids.Add(reader.GetInt32(0));
            }

            return ids;
        }
        catch (MySqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    public async Task<ClaimedSynchronization?> ClaimAsync(int id, DateTime utcNow, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            var company = await ConnectorCompany.RequireAsync(connection, cancellationToken).ConfigureAwait(false);
            await using var command = new MySqlCommand(
                """
                UPDATE synchronization
                SET next_run_at = DATE_ADD(@now, INTERVAL (timeout_seconds + 60) SECOND),
                    updated_at = @now
                WHERE id = @id
                  AND company = @company
                  AND next_run_at IS NOT NULL
                  AND next_run_at <= @now
                """,
                connection);
            command.Parameters.AddWithValue("@now", utcNow);
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@company", company);
            var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected == 0)
            {
                return null;
            }

            var row = await ReadAsync(connection, company, id, cancellationToken).ConfigureAwait(false);
            if (row is null)
            {
                return null;
            }

            return new ClaimedSynchronization
            {
                Id = row.Id,
                Direction = row.Direction,
                Source = row.Source,
                Destination = row.Destination,
                TimeoutSeconds = row.TimeoutSeconds
            };
        }
        catch (MySqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    public async Task CompleteAsync(
        int id,
        bool success,
        TimeSpan successInterval,
        TimeSpan retryDelay,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            var company = await ConnectorCompany.RequireAsync(connection, cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var current = await ReadQueueStateAsync(connection, transaction, company, id, cancellationToken).ConfigureAwait(false);
            if (current is null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            var keepForcedRun = current.NextRunAt is { } due && due <= utcNow;
            var status = success ? "success" : "failed";
            var retryCount = success ? 0 : current.RetryCount + 1;
            DateTime? nextRunAt;
            if (keepForcedRun)
            {
                nextRunAt = current.NextRunAt;
            }
            else if (success)
            {
                nextRunAt = utcNow.Add(successInterval);
            }
            else if (retryCount > current.MaxRetries)
            {
                nextRunAt = null;
            }
            else
            {
                nextRunAt = utcNow.Add(retryDelay);
            }

            await using var command = new MySqlCommand(
                """
                UPDATE synchronization
                SET status = @status,
                    retry_count = @retryCount,
                    next_run_at = @nextRunAt,
                    updated_at = @now
                WHERE id = @id AND company = @company
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue("@status", status);
            command.Parameters.AddWithValue("@retryCount", retryCount);
            command.Parameters.AddWithValue("@nextRunAt", (object?)nextRunAt ?? DBNull.Value);
            command.Parameters.AddWithValue("@now", utcNow);
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@company", company);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    private static NormalizedSynchronization Normalize(SaveSynchronizationRequest request) =>
        SynchronizationRules.Normalize(
            request.Direction,
            request.Source,
            request.Destination,
            request.MappingTableId,
            request.Code,
            request.Description,
            request.MaxRetries,
            request.TimeoutSeconds,
            request.NextRunAt);

    private static async Task<SynchronizationRecordDto?> ReadAsync(
        MySqlConnection connection,
        string company,
        int id,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(
            $"""
            SELECT {SelectColumns}
            FROM synchronization
            WHERE id = @id AND company = @company
            """,
            connection);
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@company", company);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadRow(reader);
    }

    private static async Task<QueueState?> ReadQueueStateAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string company,
        int id,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(
            """
            SELECT max_retries, retry_count, next_run_at
            FROM synchronization
            WHERE id = @id AND company = @company
            FOR UPDATE
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@company", company);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new QueueState(
            reader.GetInt32("max_retries"),
            reader.GetInt32("retry_count"),
            ReadUtcOrNull(reader, "next_run_at"));
    }

    private static SynchronizationRecordDto ReadRow(MySqlDataReader reader) =>
        new()
        {
            Id = reader.GetInt32("id"),
            Direction = reader.GetString("direction"),
            Source = reader.GetString("source"),
            Destination = reader.GetString("destination"),
            MappingTableId = reader.GetInt32("id_mapping_table"),
            Code = reader.GetString("code"),
            Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString("description"),
            Status = reader.IsDBNull(reader.GetOrdinal("status")) ? null : reader.GetString("status"),
            MaxRetries = reader.GetInt32("max_retries"),
            TimeoutSeconds = reader.GetInt32("timeout_seconds"),
            NextRunAt = ToOffset(ReadUtcOrNull(reader, "next_run_at")),
            RetryCount = reader.GetInt32("retry_count"),
            CreatedAt = ToOffset(ReadUtc(reader, "created_at")),
            UpdatedAt = ToOffset(ReadUtc(reader, "updated_at"))
        };

    private static async Task EnsureMappingAsync(
        MySqlConnection connection,
        string company,
        int mappingTableId,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(
            """
            SELECT COUNT(*)
            FROM mapping_table
            WHERE id = @id AND company = @company
            """,
            connection);
        command.Parameters.AddWithValue("@id", mappingTableId);
        command.Parameters.AddWithValue("@company", company);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (count == 0)
        {
            throw new ArgumentException("Choose a mapping for this company.");
        }
    }

    private static void Bind(
        MySqlCommand command,
        string company,
        NormalizedSynchronization values,
        string actor,
        DateTime now,
        DateTime? nextRunAt)
    {
        command.Parameters.AddWithValue("@company", company);
        command.Parameters.AddWithValue("@direction", values.Direction);
        command.Parameters.AddWithValue("@source", values.Source);
        command.Parameters.AddWithValue("@destination", values.Destination);
        command.Parameters.AddWithValue("@mappingTableId", values.MappingTableId);
        command.Parameters.AddWithValue("@code", values.Code);
        command.Parameters.AddWithValue("@description", (object?)values.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("@maxRetries", values.MaxRetries);
        command.Parameters.AddWithValue("@timeoutSeconds", values.TimeoutSeconds);
        command.Parameters.AddWithValue("@nextRunAt", (object?)nextRunAt ?? DBNull.Value);
        command.Parameters.AddWithValue("@actor", actor);
        command.Parameters.AddWithValue("@now", now);
    }

    private static DateTime ReadUtc(MySqlDataReader reader, string column) =>
        DateTime.SpecifyKind(reader.GetDateTime(column), DateTimeKind.Utc);

    private static DateTime? ReadUtcOrNull(MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);
    }

    private static DateTimeOffset ToOffset(DateTime value) => new(value);

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value is null ? null : new DateTimeOffset(value.Value);

    private async Task<MySqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private sealed record QueueState(int MaxRetries, int RetryCount, DateTime? NextRunAt);
}
