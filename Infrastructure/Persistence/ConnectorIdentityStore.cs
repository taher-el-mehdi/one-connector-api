using System.Security.Cryptography;
using System.Text;
using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;

namespace DocuWareSageConnector.Infrastructure.Persistence;

public sealed class ConnectorIdentityStore : IConnectorIdentity
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
    private readonly string _connectionString;
    private readonly byte[] _secretsKey;

    public ConnectorIdentityStore(IConfiguration configuration)
    {
        _connectionString = ConnectorStoreConnections.RequireConnectionString(configuration);
        _secretsKey = SecretProtector.ReadKey(configuration["ConnectorStore:SecretsKey"]);
    }

    public async Task<LoginResponse?> LoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        var normalized = username.Trim();
        if (normalized.Length is < 1 or > 128 || password.Length is < 1 or > 256)
        {
            OperatorPasswordHasher.VerifyDummy(password);
            return null;
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        string? userId;
        string? storedUsername;
        string? displayName;
        string? passwordHash;
        var active = false;
        await using (var find = new SqlCommand(
            """
            SELECT TOP (1) id, username, display_name, password_hash, is_active
            FROM [user]
            WHERE username = @username
            """,
            connection))
        {
            find.Parameters.AddWithValue("@username", normalized);
            await using var reader = await find.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                OperatorPasswordHasher.VerifyDummy(password);
                return null;
            }

            userId = reader.GetString("id");
            storedUsername = reader.GetString("username");
            displayName = ReadText(reader, "display_name");
            passwordHash = reader.GetString("password_hash");
            active = reader.GetBoolean("is_active");
        }

        if (passwordHash is null
            || !PasswordMatches(passwordHash, password)
            || !active
            || userId is null
            || storedUsername is null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var expires = now.Add(SessionLifetime);
        await using (var touch = new SqlCommand(
            """
            UPDATE [user]
            SET last_login_at = @now, updated_at = @now
            WHERE id = @userId
            """,
            connection))
        {
            touch.Parameters.AddWithValue("@now", now);
            touch.Parameters.AddWithValue("@userId", userId);
            await touch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var token = SessionToken.Issue(Guid.Parse(userId), storedUsername, new DateTimeOffset(DateTime.SpecifyKind(expires, DateTimeKind.Utc)), _secretsKey);

        return new LoginResponse
        {
            Username = storedUsername,
            DisplayName = displayName,
            Token = token,
            ExpiresAt = new DateTimeOffset(DateTime.SpecifyKind(expires, DateTimeKind.Utc))
        };
    }

    public async Task<OperatorProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(
            """
            SELECT TOP (1) id, username, display_name, email, is_active, created_at, updated_at, last_login_at
            FROM [user]
            WHERE id = @id AND is_active = 1
            """,
            connection);
        command.Parameters.AddWithValue("@id", userId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new OperatorProfile
        {
            Id = Guid.Parse(reader.GetString("id")),
            Username = reader.GetString("username"),
            DisplayName = ReadText(reader, "display_name"),
            Email = ReadText(reader, "email"),
            IsActive = reader.GetBoolean("is_active"),
            CreatedAt = ReadUtc(reader, "created_at"),
            UpdatedAt = ReadUtc(reader, "updated_at"),
            LastLoginAt = ReadUtcOrNull(reader, "last_login_at")
        };
    }

    public async Task<AuthenticatedOperator?> AuthenticateAsync(string token, CancellationToken cancellationToken)
    {
        if (token.Length is < 1 or > 2048)
        {
            return null;
        }

        var userId = SessionToken.ReadUserId(token, _secretsKey);
        if (userId is null)
        {
            return null;
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(
            """
            SELECT TOP (1) id, username
            FROM [user]
            WHERE id = @id AND is_active = 1
            """,
            connection);
        command.Parameters.AddWithValue("@id", userId.Value.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new AuthenticatedOperator
        {
            UserId = Guid.Parse(reader.GetString("id")),
            Username = reader.GetString("username"),
            ExpiresAt = DateTimeOffset.UtcNow.Add(SessionLifetime)
        };
    }

    public async Task<bool> VerifyPasswordAsync(Guid userId, string username, string password, CancellationToken cancellationToken)
    {
        if (password.Length is < 1 or > 256)
        {
            OperatorPasswordHasher.VerifyDummy(password.Length == 0 ? " " : password);
            return false;
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(
            """
            SELECT TOP (1) password_hash
            FROM [user]
            WHERE is_active <> 0 AND (id = @id OR username = @username)
            """,
            connection);
        command.Parameters.AddWithValue("@id", userId.ToString("D"));
        command.Parameters.AddWithValue("@username", username.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            OperatorPasswordHasher.VerifyDummy(password);
            return false;
        }

        return PasswordMatches(reader.GetString("password_hash"), password);
    }

    private bool PasswordMatches(string stored, string password)
    {
        var hash = stored.Trim();
        if (hash.StartsWith("v1.", StringComparison.Ordinal))
        {
            try
            {
                var plain = SecretProtector.Unprotect(hash, _secretsKey);
                var left = Encoding.UTF8.GetBytes(plain);
                var right = Encoding.UTF8.GetBytes(password);
                return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
            }
            catch (Exception exception) when (exception is InvalidOperationException or CryptographicException or FormatException)
            {
                return false;
            }
        }

        return OperatorPasswordHasher.Verify(hash, password);
    }

    public Task LogoutAsync(string token, CancellationToken cancellationToken) => Task.CompletedTask;

    private static string? ReadText(SqlDataReader reader, string column)
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
