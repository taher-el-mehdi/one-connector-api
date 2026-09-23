using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class ConnectorStoreConnections
{
    public static string RequireConnectionString(IConfiguration configuration)
    {
        var section = configuration.GetSection("ConnectorStore");
        var host = section["Host"]?.Trim() ?? string.Empty;
        var database = section["Database"]?.Trim() ?? string.Empty;
        var user = section["User"]?.Trim() ?? string.Empty;
        var password = section["Password"] ?? string.Empty;
        if (host.Length == 0 || database.Length == 0 || user.Length == 0 || password.Length == 0)
        {
            throw new InvalidOperationException(
                "MySQL connector store settings are missing. Set ConnectorStore__Host, ConnectorStore__Port, ConnectorStore__Database, ConnectorStore__User, and ConnectorStore__Password in .env.");
        }

        var port = 3306u;
        var portText = section["Port"];
        if (!string.IsNullOrWhiteSpace(portText) && (!uint.TryParse(portText, out port) || port == 0))
        {
            throw new InvalidOperationException("ConnectorStore:Port must be a positive number.");
        }

        var sslText = section["SslMode"];
        if (!Enum.TryParse<MySqlSslMode>(string.IsNullOrWhiteSpace(sslText) ? "Preferred" : sslText, true, out var sslMode))
        {
            throw new InvalidOperationException("ConnectorStore:SslMode must be a MySQL SSL mode such as Preferred, Required, or None.");
        }

        return new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = port,
            Database = database,
            UserID = user,
            Password = password,
            SslMode = sslMode,
            TreatTinyAsBoolean = true,
            GuidFormat = MySqlGuidFormat.None,
            AllowPublicKeyRetrieval = sslMode is MySqlSslMode.None or MySqlSslMode.Preferred,
            ConnectionTimeout = 15,
            CharacterSet = "utf8mb4"
        }.ConnectionString;
    }
}
