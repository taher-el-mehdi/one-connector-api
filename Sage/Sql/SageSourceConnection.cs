using DocuWareSageConnector.Domain.Enums;
using DocuWareSageConnector.Infrastructure.Configuration;
using DocuWareSageConnector.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DocuWareSageConnector.Sage.Sql;

public sealed class SageSourceConnection
{
    private readonly string _storeConnectionString;
    private readonly byte[] _secretsKey;

    public SageSourceConnection(IConfiguration configuration)
    {
        _storeConnectionString = ConnectorStoreConnections.RequireConnectionString(configuration);
        _secretsKey = SecretProtector.ReadKey(configuration["ConnectorStore:SecretsKey"]);
    }

    public async Task<OpenedSageSource> OpenAsync(string configurationCode, CancellationToken cancellationToken)
    {
        var code = configurationCode.Trim();
        if (code.Length == 0)
        {
            throw new InvalidOperationException("The synchronization source has no Sage configuration code.");
        }

        await using var store = new SqlConnection(_storeConnectionString);
        await store.OpenAsync(cancellationToken).ConfigureAwait(false);
        var options = ConnectorSettingsReader.ReadSage(store, _secretsKey, code);
        if (string.IsNullOrWhiteSpace(options.Server) || string.IsNullOrWhiteSpace(options.Database))
        {
            throw new InvalidOperationException($"Sage configuration '{code}' has no server or database.");
        }

        var connection = new SqlConnection(BuildConnectionString(options));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return new OpenedSageSource(connection, Math.Max(options.CommandTimeoutSeconds, 1), code);
    }

    private static string BuildConnectionString(SageOptions options)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = options.Server,
            InitialCatalog = options.Database,
            TrustServerCertificate = options.TrustServerCertificate,
            ConnectTimeout = Math.Max(options.CommandTimeoutSeconds, 5)
        };
        if (options.Authentication == SageAuthenticationMode.Sql)
        {
            builder.IntegratedSecurity = false;
            builder.UserID = options.UserName;
            builder.Password = options.Password;
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }
}

public sealed class OpenedSageSource : IAsyncDisposable
{
    public OpenedSageSource(SqlConnection connection, int commandTimeoutSeconds, string configurationCode)
    {
        Connection = connection;
        CommandTimeoutSeconds = commandTimeoutSeconds;
        ConfigurationCode = configurationCode;
    }

    public SqlConnection Connection { get; }

    public int CommandTimeoutSeconds { get; }

    public string ConfigurationCode { get; }

    public ValueTask DisposeAsync() => Connection.DisposeAsync();
}
