using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Application.Synchronization;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;

namespace DocuWareSageConnector.Infrastructure.Persistence;

public sealed class SynchronizationStore : ISynchronizationStore
{
    private const string SelectColumns =
        """
        s.id, s.direction, s.source, s.destination, s.id_mapping_table, s.code, s.description, s.status,
        s.max_retries, s.timeout_seconds, s.created_at, s.updated_at,
        s.recurrence_enabled, s.recurrence_type,
        s.recurrence_mondays, s.recurrence_tuesdays, s.recurrence_wednesdays, s.recurrence_thursdays,
        s.recurrence_fridays, s.recurrence_saturdays, s.recurrence_sundays, s.recurrence_time,
        s.interval_value, s.interval_unit, s.timezone,
        pending.start_at AS next_run_at,
        COALESCE(retries.retry_count, 0) AS retry_count
        """;

    private const string SelectFrom =
        """
        FROM synchronization AS s
        OUTER APPLY (
            SELECT TOP (1) start_at
            FROM synchronization_run
            WHERE synchronization_id = s.id AND status = N'pending'
            ORDER BY start_at, id
        ) AS pending
        OUTER APPLY (
            SELECT COUNT(*) AS retry_count
            FROM synchronization_run AS failed
            WHERE failed.synchronization_id = s.id
              AND failed.status = N'failed'
              AND failed.created_at > COALESCE((
                    SELECT MAX(ok.created_at)
                    FROM synchronization_run AS ok
                    WHERE ok.synchronization_id = s.id AND ok.status = N'success'
                ), CONVERT(datetime2, '0001-01-01'))
        ) AS retries
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
            await using var command = new SqlCommand(
                $"""
                SELECT {SelectColumns}
                {SelectFrom}
                ORDER BY s.id
                """,
                connection);
            var rows = new List<SynchronizationRecordDto>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(ReadRow(reader));
            }

            return new SynchronizationListDto { Synchronizations = rows };
        }
        catch (SqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    public async Task<SynchronizationRecordDto?> GetAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            return await ReadAsync(connection, id, cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException ex)
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
            await EnsureMappingAsync(connection, values.MappingTableId, cancellationToken).ConfigureAwait(false);
            await EnsureConfigurationAsync(connection, values.Source, "Source", cancellationToken).ConfigureAwait(false);
            await EnsureConfigurationAsync(connection, values.Destination, "Destination", cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(
                """
                INSERT INTO synchronization
                    (direction, source, destination, id_mapping_table, code, description, status,
                     max_retries, timeout_seconds, created_by, created_at, updated_at, updated_by,
                     recurrence_enabled, recurrence_type,
                     recurrence_mondays, recurrence_tuesdays, recurrence_wednesdays, recurrence_thursdays,
                     recurrence_fridays, recurrence_saturdays, recurrence_sundays, recurrence_time,
                     interval_value, interval_unit, timezone)
                OUTPUT INSERTED.id
                VALUES
                    (@direction, @source, @destination, @mappingTableId, @code, @description, NULL,
                     @maxRetries, @timeoutSeconds, @actor, @now, @now, @actor,
                     @recurrenceEnabled, @recurrenceType,
                     @recurrenceMondays, @recurrenceTuesdays, @recurrenceWednesdays, @recurrenceThursdays,
                     @recurrenceFridays, @recurrenceSaturdays, @recurrenceSundays, @recurrenceTime,
                     @intervalValue, @intervalUnit, @timezone)
                """,
                connection);
            Bind(command, values, actor, now);
            var id = SqlStore.InsertedId(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            await InsertPendingRunAsync(connection, null, id, nextRunAt, cancellationToken).ConfigureAwait(false);
            return await ReadAsync(connection, id, cancellationToken).ConfigureAwait(false)
                   ?? throw new SynchronizationStoreException(
                       "The synchronization was saved but could not be read back.",
                       new InvalidOperationException());
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (SqlException ex) when (SqlStore.IsDuplicateKey(ex))
        {
            throw new ArgumentException("A synchronization with this code already exists.");
        }
        catch (SqlException ex)
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
            if (await ReadAsync(connection, id, cancellationToken).ConfigureAwait(false) is null)
            {
                return null;
            }

            await EnsureMappingAsync(connection, values.MappingTableId, cancellationToken).ConfigureAwait(false);
            await EnsureConfigurationAsync(connection, values.Source, "Source", cancellationToken).ConfigureAwait(false);
            await EnsureConfigurationAsync(connection, values.Destination, "Destination", cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(
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
                    recurrence_enabled = @recurrenceEnabled,
                    recurrence_type = @recurrenceType,
                    recurrence_mondays = @recurrenceMondays,
                    recurrence_tuesdays = @recurrenceTuesdays,
                    recurrence_wednesdays = @recurrenceWednesdays,
                    recurrence_thursdays = @recurrenceThursdays,
                    recurrence_fridays = @recurrenceFridays,
                    recurrence_saturdays = @recurrenceSaturdays,
                    recurrence_sundays = @recurrenceSundays,
                    recurrence_time = @recurrenceTime,
                    interval_value = @intervalValue,
                    interval_unit = @intervalUnit,
                    timezone = @timezone,
                    updated_at = @now,
                    updated_by = @actor
                WHERE id = @id
                """,
                connection);
            Bind(command, values, actor, now);
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await ReplacePendingRunAsync(connection, id, values.NextRunAt, cancellationToken).ConfigureAwait(false);
            return await ReadAsync(connection, id, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (SqlException ex) when (SqlStore.IsDuplicateKey(ex))
        {
            throw new ArgumentException("A synchronization with this code already exists.");
        }
        catch (SqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(
                """
                DELETE FROM synchronization_record WHERE synchronization_id = @id;
                DELETE FROM synchronization_run WHERE synchronization_id = @id;
                DELETE FROM synchronization WHERE id = @id;
                """,
                connection);
            command.Parameters.AddWithValue("@id", id);
            var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return affected > 0;
        }
        catch (SqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    public async Task<SynchronizationFilterListDto?> ListFiltersAsync(int synchronizationId, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            if (await ReadAsync(connection, synchronizationId, cancellationToken).ConfigureAwait(false) is null)
            {
                return null;
            }

            return new SynchronizationFilterListDto
            {
                Filters = await ReadFiltersAsync(connection, synchronizationId, cancellationToken).ConfigureAwait(false)
            };
        }
        catch (SqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    public async Task<SynchronizationFilterDto?> CreateFilterAsync(
        int synchronizationId,
        SaveSynchronizationFilterRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var values = SynchronizationFilterRules.Normalize(request.FieldName, request.Operator, request.Value, request.LogicalOperator);
        var now = DateTime.UtcNow;
        var actor = userId.ToString("D");
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            if (await ReadAsync(connection, synchronizationId, cancellationToken).ConfigureAwait(false) is null)
            {
                return null;
            }

            var sortOrder = await NextSortOrderAsync(connection, synchronizationId, cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(
                """
                INSERT INTO synchronization_filter
                    (synchronization_id, field_name, operator, value, logical_operator, sort_order,
                     created_by, created_at, updated_at, updated_by)
                OUTPUT INSERTED.id
                VALUES
                    (@synchronizationId, @fieldName, @operator, @value, @logicalOperator, @sortOrder,
                     @actor, @now, @now, @actor)
                """,
                connection);
            BindFilter(command, synchronizationId, values, sortOrder, actor, now);
            var id = SqlStore.InsertedId(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            return await ReadFilterAsync(connection, synchronizationId, id, cancellationToken).ConfigureAwait(false)
                   ?? throw new SynchronizationStoreException(
                       "The filter was saved but could not be read back.",
                       new InvalidOperationException());
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (SqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    public async Task<SynchronizationFilterDto?> UpdateFilterAsync(
        int synchronizationId,
        int filterId,
        SaveSynchronizationFilterRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var values = SynchronizationFilterRules.Normalize(request.FieldName, request.Operator, request.Value, request.LogicalOperator);
        var now = DateTime.UtcNow;
        var actor = userId.ToString("D");
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            var existing = await ReadFilterAsync(connection, synchronizationId, filterId, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                return null;
            }

            await using var command = new SqlCommand(
                """
                UPDATE synchronization_filter
                SET field_name = @fieldName,
                    operator = @operator,
                    value = @value,
                    logical_operator = @logicalOperator,
                    updated_at = @now,
                    updated_by = @actor
                WHERE id = @id AND synchronization_id = @synchronizationId
                """,
                connection);
            BindFilter(command, synchronizationId, values, existing.SortOrder, actor, now);
            command.Parameters.AddWithValue("@id", filterId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return await ReadFilterAsync(connection, synchronizationId, filterId, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (SqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    public async Task<bool> DeleteFilterAsync(int synchronizationId, int filterId, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(
                """
                DELETE FROM synchronization_filter
                WHERE id = @id AND synchronization_id = @synchronizationId
                """,
                connection);
            command.Parameters.AddWithValue("@id", filterId);
            command.Parameters.AddWithValue("@synchronizationId", synchronizationId);
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
        }
        catch (SqlException ex)
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
            await using var command = new SqlCommand(
                """
                UPDATE synchronization
                SET updated_at = @now,
                    updated_by = @actor
                WHERE id = @id
                """,
                connection);
            command.Parameters.AddWithValue("@now", now);
            command.Parameters.AddWithValue("@actor", userId.ToString("D"));
            command.Parameters.AddWithValue("@id", id);
            var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected == 0)
            {
                return null;
            }

            await InsertPendingRunAsync(connection, null, id, now, cancellationToken).ConfigureAwait(false);
            return await ReadAsync(connection, id, cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    public async Task<IReadOnlyList<int>> ListDueIdsAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(
                """
                SELECT synchronization_id
                FROM synchronization_run
                WHERE status = N'pending'
                  AND start_at <= @now
                ORDER BY start_at, synchronization_id
                """,
                connection);
            command.Parameters.AddWithValue("@now", utcNow);
            var ids = new List<int>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                ids.Add(reader.GetInt32(0));
            }

            return ids;
        }
        catch (SqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    public async Task<ClaimedSynchronization?> ClaimAsync(int id, DateTime utcNow, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(
                """
                UPDATE synchronization_run
                SET status = N'running',
                    start_at = @now
                OUTPUT INSERTED.id
                WHERE id = (
                    SELECT TOP (1) id
                    FROM synchronization_run
                    WHERE synchronization_id = @id
                      AND status = N'pending'
                      AND start_at <= @now
                    ORDER BY start_at, id
                )
                """,
                connection);
            command.Parameters.AddWithValue("@now", utcNow);
            command.Parameters.AddWithValue("@id", id);
            var claimedId = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (claimedId is not Guid runId)
            {
                return null;
            }

            var row = await ReadAsync(connection, id, cancellationToken).ConfigureAwait(false);
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
                TimeoutSeconds = row.TimeoutSeconds,
                RunId = runId
            };
        }
        catch (SqlException ex)
        {
            throw new SynchronizationStoreException(ex.Message, ex);
        }
    }

    public async Task CompleteAsync(
        int id,
        bool success,
        SynchronizationRunCounts counts,
        TimeSpan successInterval,
        TimeSpan retryDelay,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var current = await ReadQueueStateAsync(connection, transaction, id, cancellationToken).ConfigureAwait(false);
            if (current is null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            var status = success ? "success" : "failed";
            var retryCount = success ? 0 : await CountFailedSinceSuccessAsync(connection, transaction, id, cancellationToken).ConfigureAwait(false) + 1;
            var schedule = await ReadRecurrenceAsync(connection, transaction, id, cancellationToken).ConfigureAwait(false);
            DateTime? nextRunAt;
            if (!success && retryCount <= current.MaxRetries)
            {
                nextRunAt = utcNow.Add(retryDelay);
            }
            else if (schedule.Enabled)
            {
                nextRunAt = RecurrenceSchedule.NextUtc(schedule, utcNow);
            }
            else if (success)
            {
                nextRunAt = utcNow.Add(successInterval);
            }
            else
            {
                nextRunAt = null;
            }

            await using var command = new SqlCommand(
                """
                UPDATE synchronization_run
                SET status = @status,
                    completed_at = @now,
                    records_inserted = @recordsInserted,
                    records_updated = @recordsUpdated,
                    error_message = @errorMessage
                WHERE id = @runId;

                UPDATE synchronization
                SET status = @status,
                    updated_at = @now
                WHERE id = @id;
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue("@status", status);
            command.Parameters.AddWithValue("@now", utcNow);
            command.Parameters.AddWithValue("@runId", current.RunId);
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@recordsInserted", counts.RecordsInserted);
            command.Parameters.AddWithValue("@recordsUpdated", counts.RecordsUpdated);
            command.Parameters.AddWithValue("@errorMessage", (object?)Truncate(counts.ErrorMessage, 4000) ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (nextRunAt is not null)
            {
                await InsertPendingRunAsync(connection, transaction, id, nextRunAt.Value, cancellationToken)
                    .ConfigureAwait(false);
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException ex)
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
            request.NextRunAt,
            request.RecurrenceEnabled,
            request.RecurrenceType,
            new RecurrenceWeekdays(
                request.RecurrenceMondays,
                request.RecurrenceTuesdays,
                request.RecurrenceWednesdays,
                request.RecurrenceThursdays,
                request.RecurrenceFridays,
                request.RecurrenceSaturdays,
                request.RecurrenceSundays),
            request.RecurrenceTime,
            request.IntervalValue,
            request.IntervalUnit,
            request.Timezone,
            DateTime.UtcNow);

    private static async Task<SynchronizationRecordDto?> ReadAsync(
        SqlConnection connection,
        int id,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            $"""
            SELECT {SelectColumns}
            {SelectFrom}
            WHERE s.id = @id
            """,
            connection);
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadRow(reader);
    }

    private static async Task<QueueState?> ReadQueueStateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int id,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT s.max_retries, run.id AS run_id
            FROM synchronization AS s
            INNER JOIN synchronization_run AS run
              ON run.synchronization_id = s.id
             AND run.status = N'running'
            WHERE s.id = @id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new QueueState(
            reader.GetGuid(reader.GetOrdinal("run_id")),
            reader.GetInt32("max_retries"));
    }

    private static SynchronizationRecordDto ReadRow(SqlDataReader reader) =>
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
            RecurrenceEnabled = reader.GetBoolean(reader.GetOrdinal("recurrence_enabled")),
            RecurrenceType = ReadStringOrNull(reader, "recurrence_type"),
            RecurrenceMondays = reader.GetBoolean(reader.GetOrdinal("recurrence_mondays")),
            RecurrenceTuesdays = reader.GetBoolean(reader.GetOrdinal("recurrence_tuesdays")),
            RecurrenceWednesdays = reader.GetBoolean(reader.GetOrdinal("recurrence_wednesdays")),
            RecurrenceThursdays = reader.GetBoolean(reader.GetOrdinal("recurrence_thursdays")),
            RecurrenceFridays = reader.GetBoolean(reader.GetOrdinal("recurrence_fridays")),
            RecurrenceSaturdays = reader.GetBoolean(reader.GetOrdinal("recurrence_saturdays")),
            RecurrenceSundays = reader.GetBoolean(reader.GetOrdinal("recurrence_sundays")),
            RecurrenceTime = ReadTimeOrNull(reader, "recurrence_time"),
            IntervalValue = reader.IsDBNull(reader.GetOrdinal("interval_value")) ? null : reader.GetInt32("interval_value"),
            IntervalUnit = ReadStringOrNull(reader, "interval_unit"),
            Timezone = ReadStringOrNull(reader, "timezone"),
            RetryCount = reader.GetInt32("retry_count"),
            CreatedAt = ToOffset(ReadUtc(reader, "created_at")),
            UpdatedAt = ToOffset(ReadUtc(reader, "updated_at"))
        };

    private static async Task<IReadOnlyList<SynchronizationFilterDto>> ReadFiltersAsync(
        SqlConnection connection,
        int synchronizationId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT id, synchronization_id, field_name, operator, value, logical_operator, sort_order, created_at, updated_at
            FROM synchronization_filter
            WHERE synchronization_id = @synchronizationId
            ORDER BY sort_order, id
            """,
            connection);
        command.Parameters.AddWithValue("@synchronizationId", synchronizationId);
        var rows = new List<SynchronizationFilterDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(ReadFilter(reader));
        }

        return rows;
    }

    private static async Task<SynchronizationFilterDto?> ReadFilterAsync(
        SqlConnection connection,
        int synchronizationId,
        int filterId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT id, synchronization_id, field_name, operator, value, logical_operator, sort_order, created_at, updated_at
            FROM synchronization_filter
            WHERE id = @id AND synchronization_id = @synchronizationId
            """,
            connection);
        command.Parameters.AddWithValue("@id", filterId);
        command.Parameters.AddWithValue("@synchronizationId", synchronizationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadFilter(reader) : null;
    }

    private static async Task<int> NextSortOrderAsync(
        SqlConnection connection,
        int synchronizationId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT COALESCE(MAX(sort_order), -1) + 1
            FROM synchronization_filter
            WHERE synchronization_id = @synchronizationId
            """,
            connection);
        command.Parameters.AddWithValue("@synchronizationId", synchronizationId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static SynchronizationFilterDto ReadFilter(SqlDataReader reader) =>
        new()
        {
            Id = reader.GetInt32("id"),
            SynchronizationId = reader.GetInt32("synchronization_id"),
            FieldName = reader.GetString("field_name"),
            Operator = reader.GetString("operator"),
            Value = reader.IsDBNull(reader.GetOrdinal("value")) ? null : reader.GetString("value"),
            LogicalOperator = reader.GetString("logical_operator"),
            SortOrder = reader.GetInt32("sort_order"),
            CreatedAt = ToOffset(ReadUtc(reader, "created_at")),
            UpdatedAt = ToOffset(ReadUtc(reader, "updated_at"))
        };

    private static void BindFilter(
        SqlCommand command,
        int synchronizationId,
        NormalizedSynchronizationFilter values,
        int sortOrder,
        string actor,
        DateTime now)
    {
        command.Parameters.AddWithValue("@synchronizationId", synchronizationId);
        command.Parameters.AddWithValue("@fieldName", values.FieldName);
        command.Parameters.AddWithValue("@operator", values.Operator);
        command.Parameters.AddWithValue("@value", (object?)values.Value ?? DBNull.Value);
        command.Parameters.AddWithValue("@logicalOperator", values.LogicalOperator);
        command.Parameters.AddWithValue("@sortOrder", sortOrder);
        command.Parameters.AddWithValue("@actor", actor);
        command.Parameters.AddWithValue("@now", now);
    }

    private static async Task EnsureMappingAsync(
        SqlConnection connection,
        int mappingTableId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM mapping_table
            WHERE id = @id
            """,
            connection);
        command.Parameters.AddWithValue("@id", mappingTableId);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (count == 0)
        {
            throw new ArgumentException("Choose a mapping.");
        }
    }

    private static async Task EnsureConfigurationAsync(
        SqlConnection connection,
        string stored,
        string label,
        CancellationToken cancellationToken)
    {
        var (code, type) = SynchronizationRules.SplitEndpoint(stored);
        await using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM setting
            WHERE type = @type AND code = @code
            """,
            connection);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@code", code);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (count == 0)
        {
            throw new ArgumentException($"Choose a {label.ToLowerInvariant()} configuration that exists in settings.");
        }
    }

    private static void Bind(
        SqlCommand command,
        NormalizedSynchronization values,
        string actor,
        DateTime now)
    {
        command.Parameters.AddWithValue("@direction", values.Direction);
        command.Parameters.AddWithValue("@source", values.Source);
        command.Parameters.AddWithValue("@destination", values.Destination);
        command.Parameters.AddWithValue("@mappingTableId", values.MappingTableId);
        command.Parameters.AddWithValue("@code", values.Code);
        command.Parameters.AddWithValue("@description", (object?)values.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("@maxRetries", values.MaxRetries);
        command.Parameters.AddWithValue("@timeoutSeconds", values.TimeoutSeconds);
        command.Parameters.AddWithValue("@recurrenceEnabled", values.Recurrence.Enabled);
        command.Parameters.AddWithValue("@recurrenceType", (object?)values.Recurrence.Type ?? DBNull.Value);
        var weekdays = values.Recurrence.Weekdays;
        command.Parameters.AddWithValue("@recurrenceMondays", weekdays.Mondays);
        command.Parameters.AddWithValue("@recurrenceTuesdays", weekdays.Tuesdays);
        command.Parameters.AddWithValue("@recurrenceWednesdays", weekdays.Wednesdays);
        command.Parameters.AddWithValue("@recurrenceThursdays", weekdays.Thursdays);
        command.Parameters.AddWithValue("@recurrenceFridays", weekdays.Fridays);
        command.Parameters.AddWithValue("@recurrenceSaturdays", weekdays.Saturdays);
        command.Parameters.AddWithValue("@recurrenceSundays", weekdays.Sundays);
        command.Parameters.Add(new SqlParameter("@recurrenceTime", System.Data.SqlDbType.Time)
        {
            Value = (object?)values.Recurrence.TimeOfDay ?? DBNull.Value
        });
        command.Parameters.AddWithValue("@intervalValue", (object?)values.Recurrence.IntervalValue ?? DBNull.Value);
        command.Parameters.AddWithValue("@intervalUnit", (object?)values.Recurrence.IntervalUnit ?? DBNull.Value);
        command.Parameters.AddWithValue("@timezone", (object?)values.Recurrence.Timezone ?? DBNull.Value);
        command.Parameters.AddWithValue("@actor", actor);
        command.Parameters.AddWithValue("@now", now);
    }

    private static async Task ReplacePendingRunAsync(
        SqlConnection connection,
        int synchronizationId,
        DateTime? nextRunAt,
        CancellationToken cancellationToken)
    {
        await using var delete = new SqlCommand(
            """
            DELETE FROM synchronization_run
            WHERE synchronization_id = @synchronizationId AND status = N'pending'
            """,
            connection);
        delete.Parameters.AddWithValue("@synchronizationId", synchronizationId);
        await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (nextRunAt is not null)
        {
            await InsertPendingRunAsync(connection, null, synchronizationId, nextRunAt.Value, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<int> CountFailedSinceSuccessAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int synchronizationId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM synchronization_run AS failed
            WHERE failed.synchronization_id = @synchronizationId
              AND failed.status = N'failed'
              AND failed.created_at > COALESCE((
                    SELECT MAX(ok.created_at)
                    FROM synchronization_run AS ok
                    WHERE ok.synchronization_id = @synchronizationId AND ok.status = N'success'
                ), CONVERT(datetime2, '0001-01-01'))
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("@synchronizationId", synchronizationId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static async Task InsertPendingRunAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int synchronizationId,
        DateTime startAt,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            INSERT INTO synchronization_run
                (synchronization_id, status, start_at, created_at)
            VALUES
                (@synchronizationId, N'pending', @startAt, SYSUTCDATETIME())
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("@synchronizationId", synchronizationId);
        command.Parameters.AddWithValue("@startAt", startAt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<NormalizedRecurrence> ReadRecurrenceAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int id,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT recurrence_enabled, recurrence_type,
                   recurrence_mondays, recurrence_tuesdays, recurrence_wednesdays, recurrence_thursdays,
                   recurrence_fridays, recurrence_saturdays, recurrence_sundays, recurrence_time,
                   interval_value, interval_unit, timezone
            FROM synchronization
            WHERE id = @id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || !reader.GetBoolean(0))
        {
            return new NormalizedRecurrence(false, null, default, null, null, null, null, null);
        }

        return new NormalizedRecurrence(
            true,
            reader.IsDBNull(1) ? null : reader.GetString(1),
            new RecurrenceWeekdays(
                reader.GetBoolean(2),
                reader.GetBoolean(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5),
                reader.GetBoolean(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8)),
            reader.IsDBNull(9) ? null : reader.GetTimeSpan(9),
            reader.IsDBNull(10) ? null : reader.GetInt32(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            null);
    }

    private static string? ReadStringOrNull(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static string? ReadTimeOrNull(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetTimeSpan(ordinal).ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return value[..max];
    }

    private static DateTime ReadUtc(SqlDataReader reader, string column) =>
        DateTime.SpecifyKind(reader.GetDateTime(column), DateTimeKind.Utc);

    private static DateTime? ReadUtcOrNull(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);
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

    private sealed record QueueState(Guid RunId, int MaxRetries);
}
