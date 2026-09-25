using DocuWareSageConnector.Application;
using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Domain.Enums;
using DocuWareSageConnector.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;

namespace DocuWareSageConnector.Infrastructure.Persistence;

public sealed class DocuWareSettingsStore : IDocuWareSettingsStore
{
    private const int TextMaxLength = 2048;
    private const int SecretMaxLength = 256;
    private readonly string _connectionString;
    private readonly byte[] _secretsKey;
    private readonly DocuWareOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DocuWareSettingsStore(IConfiguration configuration, IOptions<DocuWareOptions> options)
    {
        _connectionString = ConnectorStoreConnections.RequireConnectionString(configuration);
        _secretsKey = SecretProtector.ReadKey(configuration["ConnectorStore:SecretsKey"]);
        _options = options.Value;
    }

    public async Task UpdateAsync(UpdateDocuWareConfigurationRequest request, Guid userId, CancellationToken cancellationToken)
    {
        var platformUrl = request.PlatformUrl.Trim();
        var organization = request.Organization.Trim();
        var userName = request.UserName.Trim();
        var clientId = request.ClientId.Trim();
        var scope = request.Scope.Trim();

        var required = await ReadRequirementsAsync(cancellationToken).ConfigureAwait(false);
        if (required.PlatformUrl && string.IsNullOrWhiteSpace(platformUrl))
        {
            throw new SettingsValidationException("platform_url_required");
        }

        if (!string.IsNullOrWhiteSpace(platformUrl)
            && (!Uri.TryCreate(platformUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            throw new SettingsValidationException("platform_url_invalid");
        }

        if (!Enum.TryParse<DocuWareAuthenticationMode>(request.AuthenticationMode, ignoreCase: true, out var mode))
        {
            throw new SettingsValidationException("authentication_invalid");
        }

        if (required.Organization && string.IsNullOrWhiteSpace(organization))
        {
            throw new SettingsValidationException("organization_required");
        }

        if (required.UserName && string.IsNullOrWhiteSpace(userName))
        {
            throw new SettingsValidationException("user_name_required");
        }

        if (required.ClientId && string.IsNullOrWhiteSpace(clientId))
        {
            throw new SettingsValidationException("client_id_required");
        }

        if (required.Scope && string.IsNullOrWhiteSpace(scope))
        {
            throw new SettingsValidationException("scope_required");
        }

        var password = request.Password ?? _options.Password;
        var clientSecret = request.ClientSecret ?? _options.ClientSecret;
        if (required.Password && string.IsNullOrEmpty(password))
        {
            throw new SettingsValidationException("password_missing");
        }

        if (required.ClientSecret && string.IsNullOrEmpty(clientSecret))
        {
            throw new SettingsValidationException("client_secret_missing");
        }

        if (platformUrl.Length > TextMaxLength
            || organization.Length > TextMaxLength
            || userName.Length > TextMaxLength
            || clientId.Length > TextMaxLength
            || scope.Length > TextMaxLength
            || request.Password is { Length: > SecretMaxLength }
            || request.ClientSecret is { Length: > SecretMaxLength })
        {
            throw new SettingsValidationException("value_too_long");
        }

        var updates = new List<(string Key, string? Value)>
        {
            (SettingKeys.PlatformUrl, platformUrl),
            (SettingKeys.Organization, organization),
            (SettingKeys.AuthenticationMode, mode.ToString()),
            (SettingKeys.UserName, userName),
            (SettingKeys.ClientId, clientId),
            (SettingKeys.Scope, scope)
        };

        if (request.Password is not null)
        {
            updates.Add((SettingKeys.PasswordProtected, SecretProtector.Protect(request.Password, _secretsKey)));
        }

        if (request.ClientSecret is not null)
        {
            updates.Add((SettingKeys.ClientSecretProtected, SecretProtector.Protect(request.ClientSecret, _secretsKey)));
        }

        var changed = platformUrl != _options.PlatformUrl.Trim()
            || organization != _options.Organization.Trim()
            || mode != _options.AuthenticationMode
            || userName != _options.UserName.Trim()
            || clientId != _options.ClientId.Trim()
            || scope != _options.Scope.Trim()
            || (request.Password is not null && request.Password != _options.Password)
            || (request.ClientSecret is not null && request.ClientSecret != _options.ClientSecret);
        var status = changed ? false : _options.Status;
        var configured = FieldFilled(required.PlatformUrl, platformUrl)
            && FieldFilled(required.Organization, organization)
            && (!required.Authentication || !string.IsNullOrWhiteSpace(request.AuthenticationMode))
            && FieldFilled(required.UserName, userName)
            && SecretFilled(required.Password, password)
            && FieldFilled(required.ClientId, clientId)
            && SecretFilled(required.ClientSecret, clientSecret)
            && FieldFilled(required.Scope, scope);
        var now = DateTime.UtcNow;
        var actor = userId.ToString("D");

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

            _options.PlatformUrl = platformUrl;
            _options.Organization = organization;
            _options.AuthenticationMode = mode;
            _options.UserName = userName;
            _options.ClientId = clientId;
            _options.Scope = scope;
            _options.Configured = configured;
            _options.Status = status;
            if (request.Password is not null)
            {
                _options.Password = request.Password;
            }

            if (request.ClientSecret is not null)
            {
                _options.ClientSecret = request.ClientSecret;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DocuWareFieldRequirements> ReadRequirementsAsync(CancellationToken cancellationToken)
    {
        var flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(
            """
            SELECT [key], [required] FROM setting
            WHERE type = @type AND code = @code
            """,
            connection);
        command.Parameters.AddWithValue("@type", SettingTable.DocuWare);
        command.Parameters.AddWithValue("@code", SettingTable.DocuWare);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            flags[reader.GetString("key")] = !reader.IsDBNull(reader.GetOrdinal("required")) && reader.GetBoolean("required");
        }

        return new DocuWareFieldRequirements
        {
            PlatformUrl = Flag(flags, SettingKeys.PlatformUrl),
            Organization = Flag(flags, SettingKeys.Organization),
            Authentication = Flag(flags, SettingKeys.AuthenticationMode),
            UserName = Flag(flags, SettingKeys.UserName),
            Password = Flag(flags, SettingKeys.PasswordProtected),
            ClientId = Flag(flags, SettingKeys.ClientId),
            ClientSecret = Flag(flags, SettingKeys.ClientSecretProtected),
            Scope = Flag(flags, SettingKeys.Scope)
        };
    }

    public async Task SetStatusAsync(bool status, Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var actor = userId.ToString("D");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(
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
            command.Parameters.AddWithValue("@type", SettingTable.DocuWare);
            command.Parameters.AddWithValue("@code", SettingTable.DocuWare);
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
        SqlConnection connection,
        SqlTransaction transaction,
        string key,
        string? value,
        string actor,
        DateTime now,
        bool configured,
        bool status,
        CancellationToken cancellationToken)
    {
        await using var update = new SqlCommand(
            """
            UPDATE setting
            SET [value] = @value,
                updated_at = @now,
                updated_by = @actor,
                configured = @configured,
                status = @status
            WHERE type = @type AND code = @code AND [key] = @key
            """,
            connection,
            transaction);
        update.Parameters.AddWithValue("@type", SettingTable.DocuWare);
        update.Parameters.AddWithValue("@code", SettingTable.DocuWare);
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

        await using var insert = new SqlCommand(
            """
            INSERT INTO setting
                (type, code, description, [key], [value], created_by, created_at, updated_at, updated_by, configured, status, [required])
            VALUES
                (@type, @code, @description, @key, @value, @actor, @now, @now, @actor, @configured, @status, 1)
            """,
            connection,
            transaction);
        insert.Parameters.AddWithValue("@type", SettingTable.DocuWare);
        insert.Parameters.AddWithValue("@code", SettingTable.DocuWare);
        insert.Parameters.AddWithValue("@description", "DocuWare");
        insert.Parameters.AddWithValue("@key", key);
        insert.Parameters.AddWithValue("@value", value is null ? DBNull.Value : value);
        insert.Parameters.AddWithValue("@actor", actor);
        insert.Parameters.AddWithValue("@now", now);
        insert.Parameters.AddWithValue("@configured", configured ? 1 : 0);
        insert.Parameters.AddWithValue("@status", status ? 1 : 0);
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task StampAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string actor,
        DateTime now,
        bool configured,
        bool status,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
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
        command.Parameters.AddWithValue("@type", SettingTable.DocuWare);
        command.Parameters.AddWithValue("@code", SettingTable.DocuWare);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
