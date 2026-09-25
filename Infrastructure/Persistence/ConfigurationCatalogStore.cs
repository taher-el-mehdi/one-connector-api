using DocuWareSageConnector.Application;
using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;

namespace DocuWareSageConnector.Infrastructure.Persistence;

public sealed class ConfigurationCatalogStore : IConfigurationCatalog
{
    private readonly string _connectionString;
    private readonly byte[] _secretsKey;
    private readonly DocuWareOptions _docuWare;
    private readonly SageOptions _sage;
    private readonly SynchronizationOptions _synchronization;

    public ConfigurationCatalogStore(
        IConfiguration configuration,
        IOptions<DocuWareOptions> docuWare,
        IOptions<SageOptions> sage,
        IOptions<SynchronizationOptions> synchronization)
    {
        _connectionString = ConnectorStoreConnections.RequireConnectionString(configuration);
        _secretsKey = SecretProtector.ReadKey(configuration["ConnectorStore:SecretsKey"]);
        _docuWare = docuWare.Value;
        _sage = sage.Value;
        _synchronization = synchronization.Value;
    }

    public async Task<IReadOnlyList<ConfigurationEntry>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(
            """
            SELECT 
                type,
                code,
                MAX(description) AS description,
                CAST(MAX(CAST(status AS INT)) AS BIT) AS status
            FROM setting
            GROUP BY type, code
            ORDER BY type, code
            """,
            connection);
        var entries = new List<ConfigurationEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var descriptionOrdinal = reader.GetOrdinal("description");
            entries.Add(new ConfigurationEntry
            {
                Type = reader.GetString("type"),
                Code = reader.GetString("code"),
                Description = reader.IsDBNull(descriptionOrdinal) ? null : reader.GetString(descriptionOrdinal),
                Status = !reader.IsDBNull(reader.GetOrdinal("status")) && reader.GetBoolean("status")
            });
        }

        return entries;
    }

    public async Task<ConfigurationEntry> CreateAsync(
        CreateConfigurationRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var type = request.Type.Trim();
        var code = request.Code.Trim();
        var description = request.Description.Trim();
        if (!SettingTable.IsConnectorType(type))
        {
            throw new SettingsValidationException("type_invalid");
        }

        if (string.IsNullOrWhiteSpace(code) || code.Length > 64)
        {
            throw new SettingsValidationException("code_invalid");
        }

        if (string.IsNullOrWhiteSpace(description) || description.Length > 512)
        {
            throw new SettingsValidationException("description_invalid");
        }

        var rows = SettingTemplates.For(type);
        if (rows.Count == 0)
        {
            throw new SettingsValidationException("type_invalid");
        }

        var now = DateTime.UtcNow;
        var actor = userId.ToString("D");
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var exists = new SqlCommand(
            """
            SELECT COUNT(*) FROM setting
            WHERE type = @type AND code = @code
            """,
            connection);
        exists.Parameters.AddWithValue("@type", type);
        exists.Parameters.AddWithValue("@code", code);
        if (Convert.ToInt32(await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) > 0)
        {
            throw new SettingsValidationException("code_taken");
        }

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var (key, value) in rows)
            {
                await using var insert = new SqlCommand(
                    """
                    INSERT INTO setting
                        (type, code, description, [key], [value], created_by, created_at, updated_at, updated_by, status)
                    VALUES
                        (@type, @code, @description, @key, @value, @actor, @now, @now, @actor, 0)
                    """,
                    connection,
                    transaction);
                insert.Parameters.AddWithValue("@type", type);
                insert.Parameters.AddWithValue("@code", code);
                insert.Parameters.AddWithValue("@description", description);
                insert.Parameters.AddWithValue("@key", key);
                insert.Parameters.AddWithValue("@value", value is null ? DBNull.Value : value);
                insert.Parameters.AddWithValue("@actor", actor);
                insert.Parameters.AddWithValue("@now", now);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        return new ConfigurationEntry
        {
            Type = type,
            Code = code,
            Description = description,
            Status = false
        };
    }

    public async Task<IReadOnlyList<ConfigurationSettingRow>> ListRowsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(
            """
            SELECT type, code, description, [key], [value], status
            FROM setting
            ORDER BY type, code, [key]
            """,
            connection);
        var rows = new List<ConfigurationSettingRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var descriptionOrdinal = reader.GetOrdinal("description");
            var valueOrdinal = reader.GetOrdinal("value");
            rows.Add(new ConfigurationSettingRow
            {
                Type = reader.GetString("type"),
                Code = reader.GetString("code"),
                Description = reader.IsDBNull(descriptionOrdinal) ? null : reader.GetString(descriptionOrdinal),
                Key = reader.GetString("key"),
                Value = reader.IsDBNull(valueOrdinal) ? null : reader.GetString(valueOrdinal),
                Status = reader.GetBoolean("status")
            });
        }

        return rows;
    }

    public async Task<ImportConfigurationResult> ImportAsync(
        IReadOnlyList<ConfigurationSettingRow> rows,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var normalized = ConfigurationImportRules.Normalize(rows);
        foreach (var row in normalized)
        {
            if (row.Value is null)
            {
                continue;
            }

            if (!row.Key.Equals(SettingKeys.PasswordProtected, StringComparison.OrdinalIgnoreCase)
                && !row.Key.Equals(SettingKeys.ClientSecretProtected, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                SecretProtector.Unprotect(row.Value, _secretsKey);
            }
            catch (InvalidOperationException)
            {
                throw new SettingsValidationException("secret_invalid");
            }
        }

        var now = DateTime.UtcNow;
        var actor = userId.ToString("D");
        var added = 0;
        var updated = 0;
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var row in normalized)
            {
                await using var update = new SqlCommand(
                    """
                    UPDATE setting
                    SET description = @description,
                        [value] = @value,
                        status = @status,
                        updated_at = @now,
                        updated_by = @actor
                    WHERE type = @type AND code = @code AND [key] = @key
                    """,
                    connection,
                    transaction);
                update.Parameters.AddWithValue("@type", row.Type);
                update.Parameters.AddWithValue("@code", row.Code);
                update.Parameters.AddWithValue("@key", row.Key);
                update.Parameters.AddWithValue("@description", row.Description is null ? DBNull.Value : row.Description);
                update.Parameters.AddWithValue("@value", row.Value is null ? DBNull.Value : row.Value);
                update.Parameters.AddWithValue("@status", row.Status ? 1 : 0);
                update.Parameters.AddWithValue("@now", now);
                update.Parameters.AddWithValue("@actor", actor);
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0)
                {
                    updated++;
                    continue;
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
                insert.Parameters.AddWithValue("@type", row.Type);
                insert.Parameters.AddWithValue("@code", row.Code);
                insert.Parameters.AddWithValue("@key", row.Key);
                insert.Parameters.AddWithValue("@description", row.Description is null ? DBNull.Value : row.Description);
                insert.Parameters.AddWithValue("@value", row.Value is null ? DBNull.Value : row.Value);
                insert.Parameters.AddWithValue("@status", row.Status ? 1 : 0);
                insert.Parameters.AddWithValue("@now", now);
                insert.Parameters.AddWithValue("@actor", actor);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                added++;
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        ConnectorSettingsReader.Apply(connection, _secretsKey, _docuWare, _sage, _synchronization);
        return new ImportConfigurationResult { Added = added, Updated = updated };
    }

}
