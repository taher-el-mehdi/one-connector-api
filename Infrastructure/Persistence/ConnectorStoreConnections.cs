using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class ConnectorStoreConnections
{
    public static string RequireConnectionString(IConfiguration configuration)
    {
        var section = configuration.GetSection("ConnectorStore");
        var server = FirstNonEmpty(section["Server"], section["Host"]);
        var database = section["Database"]?.Trim() ?? string.Empty;
        if (server.Length == 0 || database.Length == 0)
        {
            throw new InvalidOperationException(
                "SQL Server connector store settings are missing. Set ConnectorStore__Server and ConnectorStore__Database in .env.");
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            TrustServerCertificate = true,
            ConnectTimeout = 15
        };

        var user = section["User"]?.Trim() ?? string.Empty;
        var password = section["Password"] ?? string.Empty;
        if (user.Length > 0)
        {
            builder.IntegratedSecurity = false;
            builder.UserID = user;
            builder.Password = password;
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }

    private static string FirstNonEmpty(string? primary, string? fallback)
    {
        var first = primary?.Trim() ?? string.Empty;
        if (first.Length > 0)
        {
            return first;
        }

        return fallback?.Trim() ?? string.Empty;
    }
}
