using System.Globalization;
using DocuWareSageConnector.Application;
using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;

namespace DocuWareSageConnector.Infrastructure.Persistence;

public sealed class SynchronizationSettingsStore : ISynchronizationSettingsStore
{
    private readonly string _connectionString;
    private readonly SynchronizationOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SynchronizationSettingsStore(IConfiguration configuration, IOptions<SynchronizationOptions> options)
    {
        _connectionString = ConnectorStoreConnections.RequireConnectionString(configuration);
        _options = options.Value;
    }

    public async Task UpdateAsync(UpdateSynchronizationConfigurationRequest request, Guid userId, CancellationToken cancellationToken)
    {
        if (request.IntervalSeconds is < 1 or > 86400)
        {
            throw new SettingsValidationException("interval_invalid");
        }

        if (request.MaxRetries is < 0 or > 100)
        {
            throw new SettingsValidationException("max_retries_invalid");
        }

        if (request.FirstRetryDelaySeconds is < 0 or > 86400)
        {
            throw new SettingsValidationException("first_retry_delay_invalid");
        }

        var changed = request.IntervalSeconds != _options.IntervalSeconds
            || request.MaxRetries != _options.MaxRetries
            || request.FirstRetryDelaySeconds != _options.FirstRetryDelaySeconds;
        var status = changed ? false : _options.Status;
        var now = DateTime.UtcNow;
        var actor = userId.ToString("D");
        var updates = new (string Key, string Value)[]
        {
            (SettingKeys.IntervalSeconds, request.IntervalSeconds.ToString(CultureInfo.InvariantCulture)),
            (SettingKeys.MaxRetries, request.MaxRetries.ToString(CultureInfo.InvariantCulture)),
            (SettingKeys.FirstRetryDelaySeconds, request.FirstRetryDelaySeconds.ToString(CultureInfo.InvariantCulture))
        };

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (var (key, value) in updates)
                {
                    await UpsertAsync(connection, transaction, key, value, actor, now, status, cancellationToken).ConfigureAwait(false);
                }

                await StampAsync(connection, transaction, actor, now, status, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }

            _options.IntervalSeconds = request.IntervalSeconds;
            _options.MaxRetries = request.MaxRetries;
            _options.FirstRetryDelaySeconds = request.FirstRetryDelaySeconds;
            _options.Status = status;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task UpsertAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string key,
        string value,
        string actor,
        DateTime now,
        bool status,
        CancellationToken cancellationToken)
    {
        await using var update = new SqlCommand(
            """
            UPDATE setting
            SET [value] = @value,
                updated_at = @now,
                updated_by = @actor,
                status = @status
            WHERE type = @type AND code = @code AND [key] = @key
            """,
            connection,
            transaction);
        update.Parameters.AddWithValue("@type", SettingTable.Synchronization);
        update.Parameters.AddWithValue("@code", SettingTable.Synchronization);
        update.Parameters.AddWithValue("@key", key);
        update.Parameters.AddWithValue("@value", value);
        update.Parameters.AddWithValue("@now", now);
        update.Parameters.AddWithValue("@actor", actor);
        update.Parameters.AddWithValue("@status", status ? 1 : 0);
        if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0)
        {
            return;
        }

        await using var insert = new SqlCommand(
            """
            INSERT INTO setting
                (type, code, description, [key], [value], created_by, created_at, updated_at, updated_by, status)
            VALUES
                (@type, @code, @description, @key, @value, @actor, @now, @now, @actor, @status)
            """,
            connection,
            transaction);
        insert.Parameters.AddWithValue("@type", SettingTable.Synchronization);
        insert.Parameters.AddWithValue("@code", SettingTable.Synchronization);
        insert.Parameters.AddWithValue("@description", "Synchronization");
        insert.Parameters.AddWithValue("@key", key);
        insert.Parameters.AddWithValue("@value", value);
        insert.Parameters.AddWithValue("@actor", actor);
        insert.Parameters.AddWithValue("@now", now);
        insert.Parameters.AddWithValue("@status", status ? 1 : 0);
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task StampAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string actor,
        DateTime now,
        bool status,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            UPDATE setting
            SET status = @status,
                updated_at = @now,
                updated_by = @actor
            WHERE type = @type AND code = @code
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("@status", status ? 1 : 0);
        command.Parameters.AddWithValue("@now", now);
        command.Parameters.AddWithValue("@actor", actor);
        command.Parameters.AddWithValue("@type", SettingTable.Synchronization);
        command.Parameters.AddWithValue("@code", SettingTable.Synchronization);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
