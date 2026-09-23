using System.Globalization;
using DocuWareSageConnector.Application;
using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using MySqlConnector;

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

        var required = await ReadRequirementsAsync(cancellationToken).ConfigureAwait(false);
        var changed = request.IntervalSeconds != _options.IntervalSeconds
            || request.MaxRetries != _options.MaxRetries
            || request.FirstRetryDelaySeconds != _options.FirstRetryDelaySeconds;
        var status = changed ? false : _options.Status;
        var configured = InRange(required.Interval, request.IntervalSeconds is >= 1 and <= 86400)
            && InRange(required.MaxRetries, request.MaxRetries is >= 0 and <= 100)
            && InRange(required.FirstRetryDelay, request.FirstRetryDelaySeconds is >= 0 and <= 86400);
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
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var company = await RequireCompanyAsync(connection, cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (var (key, value) in updates)
                {
                    await UpsertAsync(connection, transaction, company, key, value, actor, now, configured, status, cancellationToken).ConfigureAwait(false);
                }

                await StampAsync(connection, transaction, company, actor, now, configured, status, cancellationToken).ConfigureAwait(false);
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
            _options.Configured = configured;
            _options.Status = status;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SynchronizationFieldRequirements> ReadRequirementsAsync(CancellationToken cancellationToken)
    {
        var flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var company = await RequireCompanyAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(
            "SELECT `key`, `required` FROM setting_synchronization WHERE company = @company",
            connection);
        command.Parameters.AddWithValue("@company", company);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            flags[reader.GetString("key")] = !reader.IsDBNull(reader.GetOrdinal("required")) && reader.GetBoolean("required");
        }

        return new SynchronizationFieldRequirements
        {
            Interval = Flag(flags, SettingKeys.IntervalSeconds),
            MaxRetries = Flag(flags, SettingKeys.MaxRetries),
            FirstRetryDelay = Flag(flags, SettingKeys.FirstRetryDelaySeconds)
        };
    }

    private static bool Flag(Dictionary<string, bool> flags, string key) =>
        flags.TryGetValue(key, out var required) && required;

    private static bool InRange(bool required, bool valid) => !required || valid;

    private static async Task<string> RequireCompanyAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using (var named = new MySqlCommand(
            "SELECT name FROM company WHERE name = @name LIMIT 1",
            connection))
        {
            named.Parameters.AddWithValue("@name", ConnectorCompany.Name);
            var value = await named.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (value is not null and not DBNull)
            {
                var company = Convert.ToString(value);
                if (!string.IsNullOrEmpty(company))
                {
                    return company;
                }
            }
        }

        await using var any = new MySqlCommand("SELECT name FROM company ORDER BY name LIMIT 1", connection);
        var fallback = await any.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var name = fallback is null or DBNull ? null : Convert.ToString(fallback);
        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidOperationException("The connector store has no company.");
        }

        return name;
    }

    private static async Task UpsertAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string company,
        string key,
        string value,
        string actor,
        DateTime now,
        bool configured,
        bool status,
        CancellationToken cancellationToken)
    {
        await using var update = new MySqlCommand(
            """
            UPDATE setting_synchronization
            SET `value` = @value,
                updated_at = @now,
                updated_by = @actor,
                configured = @configured,
                status = @status
            WHERE company = @company AND `key` = @key
            """,
            connection,
            transaction);
        update.Parameters.AddWithValue("@company", company);
        update.Parameters.AddWithValue("@key", key);
        update.Parameters.AddWithValue("@value", value);
        update.Parameters.AddWithValue("@now", now);
        update.Parameters.AddWithValue("@actor", actor);
        update.Parameters.AddWithValue("@configured", configured ? 1 : 0);
        update.Parameters.AddWithValue("@status", status ? 1 : 0);
        if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0)
        {
            return;
        }

        await using var insert = new MySqlCommand(
            """
            INSERT INTO setting_synchronization
                (company, `key`, `value`, created_by, created_at, updated_at, updated_by, configured, status, `required`)
            VALUES
                (@company, @key, @value, @actor, @now, @now, @actor, @configured, @status, 1)
            """,
            connection,
            transaction);
        insert.Parameters.AddWithValue("@company", company);
        insert.Parameters.AddWithValue("@key", key);
        insert.Parameters.AddWithValue("@value", value);
        insert.Parameters.AddWithValue("@actor", actor);
        insert.Parameters.AddWithValue("@now", now);
        insert.Parameters.AddWithValue("@configured", configured ? 1 : 0);
        insert.Parameters.AddWithValue("@status", status ? 1 : 0);
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task StampAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string company,
        string actor,
        DateTime now,
        bool configured,
        bool status,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(
            """
            UPDATE setting_synchronization
            SET configured = @configured,
                status = @status,
                updated_at = @now,
                updated_by = @actor
            WHERE company = @company
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("@configured", configured ? 1 : 0);
        command.Parameters.AddWithValue("@status", status ? 1 : 0);
        command.Parameters.AddWithValue("@now", now);
        command.Parameters.AddWithValue("@actor", actor);
        command.Parameters.AddWithValue("@company", company);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
