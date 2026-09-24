using DocuWareSageConnector.Application;
using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Domain.Enums;
using DocuWareSageConnector.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace DocuWareSageConnector.Infrastructure.Persistence;

public sealed class SageSettingsStore : ISageSettingsStore
{
    private const int TextMaxLength = 2048;
    private const int SecretMaxLength = 256;
    private readonly string _connectionString;
    private readonly byte[] _secretsKey;
    private readonly SageOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SageSettingsStore(IConfiguration configuration, IOptions<SageOptions> options)
    {
        _connectionString = ConnectorStoreConnections.RequireConnectionString(configuration);
        _secretsKey = SecretProtector.ReadKey(configuration["ConnectorStore:SecretsKey"]);
        _options = options.Value;
    }

    public async Task UpdateAsync(UpdateSageConfigurationRequest request, Guid userId, CancellationToken cancellationToken)
    {
        var server = request.Server.Trim();
        var database = request.Database.Trim();
        var userName = request.UserName.Trim();
        var required = await ReadRequirementsAsync(cancellationToken).ConfigureAwait(false);
        if (required.Server && string.IsNullOrWhiteSpace(server))
        {
            throw new SettingsValidationException("server_required");
        }

        if (required.Database && string.IsNullOrWhiteSpace(database))
        {
            throw new SettingsValidationException("database_required");
        }

        if (!Enum.TryParse<SageAuthenticationMode>(request.Authentication, ignoreCase: true, out var authentication))
        {
            throw new SettingsValidationException("authentication_invalid");
        }

        if (required.UserName && string.IsNullOrWhiteSpace(userName))
        {
            throw new SettingsValidationException("user_name_required");
        }

        var password = request.Password ?? _options.Password;
        if (required.Password && string.IsNullOrEmpty(password))
        {
            throw new SettingsValidationException("password_missing");
        }

        if (request.CommandTimeoutSeconds is < 1 or > 3600)
        {
            throw new SettingsValidationException("command_timeout_invalid");
        }

        if (server.Length > TextMaxLength
            || database.Length > TextMaxLength
            || userName.Length > TextMaxLength
            || request.Password is { Length: > SecretMaxLength })
        {
            throw new SettingsValidationException("value_too_long");
        }

        var updates = new List<(string Key, string? Value)>
        {
            (SettingKeys.ServerName, server),
            (SettingKeys.DatabaseName, database),
            (SettingKeys.AuthentificationMode, authentication.ToString()),
            (SettingKeys.UserName, userName),
            (SettingKeys.CommandTimeoutSeconds, request.CommandTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
        if (request.Password is not null)
        {
            updates.Add((SettingKeys.PasswordProtected, SecretProtector.Protect(request.Password, _secretsKey)));
        }

        var changed = server != _options.Server.Trim()
            || database != _options.Database.Trim()
            || authentication != _options.Authentication
            || userName != _options.UserName.Trim()
            || request.CommandTimeoutSeconds != _options.CommandTimeoutSeconds
            || (request.Password is not null && request.Password != _options.Password);
        var status = changed ? false : _options.Status;
        var configured = FieldFilled(required.Server, server)
            && FieldFilled(required.Database, database)
            && (!required.Authentication || !string.IsNullOrWhiteSpace(request.Authentication))
            && FieldFilled(required.UserName, userName)
            && SecretFilled(required.Password, password)
            && (!required.CommandTimeout || request.CommandTimeoutSeconds is >= 1 and <= 3600);
        var now = DateTime.UtcNow;
        var actor = userId.ToString("D");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (var (key, value) in updates)
                {
                    await UpsertAsync(connection, transaction, key, value, actor, now, configured, status, cancellationToken).ConfigureAwait(false);
                }

                await StampAsync(connection, transaction, actor, now, configured, status, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }

            _options.Server = server;
            _options.Database = database;
            _options.Authentication = authentication;
            _options.UserName = userName;
            _options.CommandTimeoutSeconds = request.CommandTimeoutSeconds;
            _options.Configured = configured;
            _options.Status = status;
            if (request.Password is not null)
            {
                _options.Password = request.Password;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SageFieldRequirements> ReadRequirementsAsync(CancellationToken cancellationToken)
    {
        var flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(
            """
            SELECT `key`, `required` FROM setting
            WHERE type = @type AND code = @code
            """,
            connection);
        command.Parameters.AddWithValue("@type", SettingTable.Sage);
        command.Parameters.AddWithValue("@code", SettingTable.Sage);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            flags[reader.GetString("key")] = !reader.IsDBNull(reader.GetOrdinal("required")) && reader.GetBoolean("required");
        }

        return new SageFieldRequirements
        {
            Server = Flag(flags, SettingKeys.ServerName),
            Database = Flag(flags, SettingKeys.DatabaseName),
            Authentication = Flag(flags, SettingKeys.AuthentificationMode),
            UserName = Flag(flags, SettingKeys.UserName),
            Password = Flag(flags, SettingKeys.PasswordProtected),
            CommandTimeout = Flag(flags, SettingKeys.CommandTimeoutSeconds)
        };
    }

    public async Task SetStatusAsync(bool status, Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var actor = userId.ToString("D");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new MySqlCommand(
                """
                UPDATE setting
                SET status = @status,
                    updated_at = @now,
                    updated_by = @actor
                WHERE type = @type AND code = @code
                """,
                connection);
            command.Parameters.AddWithValue("@status", status ? 1 : 0);
            command.Parameters.AddWithValue("@now", now);
            command.Parameters.AddWithValue("@actor", actor);
            command.Parameters.AddWithValue("@type", SettingTable.Sage);
            command.Parameters.AddWithValue("@code", SettingTable.Sage);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _options.Status = status;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool Flag(Dictionary<string, bool> flags, string key) =>
        flags.TryGetValue(key, out var required) && required;

    private static bool FieldFilled(bool required, string value) =>
        !required || !string.IsNullOrWhiteSpace(value);

    private static bool SecretFilled(bool required, string value) =>
        !required || !string.IsNullOrEmpty(value);

    private static async Task UpsertAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string key,
        string? value,
        string actor,
        DateTime now,
        bool configured,
        bool status,
        CancellationToken cancellationToken)
    {
        await using var update = new MySqlCommand(
            """
            UPDATE setting
            SET `value` = @value,
                updated_at = @now,
                updated_by = @actor,
                configured = @configured,
                status = @status
            WHERE type = @type AND code = @code AND `key` = @key
            """,
            connection,
            transaction);
        update.Parameters.AddWithValue("@type", SettingTable.Sage);
        update.Parameters.AddWithValue("@code", SettingTable.Sage);
        update.Parameters.AddWithValue("@key", key);
        update.Parameters.AddWithValue("@value", value is null ? DBNull.Value : value);
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
            INSERT INTO setting
                (type, code, description, `key`, `value`, created_by, created_at, updated_at, updated_by, configured, status, `required`)
            VALUES
                (@type, @code, @description, @key, @value, @actor, @now, @now, @actor, @configured, @status, 1)
            """,
            connection,
            transaction);
        insert.Parameters.AddWithValue("@type", SettingTable.Sage);
        insert.Parameters.AddWithValue("@code", SettingTable.Sage);
        insert.Parameters.AddWithValue("@description", "Sage");
        insert.Parameters.AddWithValue("@key", key);
        insert.Parameters.AddWithValue("@value", value is null ? DBNull.Value : value);
        insert.Parameters.AddWithValue("@actor", actor);
        insert.Parameters.AddWithValue("@now", now);
        insert.Parameters.AddWithValue("@configured", configured ? 1 : 0);
        insert.Parameters.AddWithValue("@status", status ? 1 : 0);
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task StampAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string actor,
        DateTime now,
        bool configured,
        bool status,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(
            """
            UPDATE setting
            SET configured = @configured,
                status = @status,
                updated_at = @now,
                updated_by = @actor
            WHERE type = @type AND code = @code
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("@configured", configured ? 1 : 0);
        command.Parameters.AddWithValue("@status", status ? 1 : 0);
        command.Parameters.AddWithValue("@now", now);
        command.Parameters.AddWithValue("@actor", actor);
        command.Parameters.AddWithValue("@type", SettingTable.Sage);
        command.Parameters.AddWithValue("@code", SettingTable.Sage);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
