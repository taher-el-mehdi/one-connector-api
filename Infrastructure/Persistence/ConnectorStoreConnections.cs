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

        ApplyLogin(
            builder,
            section["LoginMode"],
            section["User"]?.Trim() ?? string.Empty,
            section["Password"] ?? string.Empty);

        return builder.ConnectionString;
    }

    private static void ApplyLogin(SqlConnectionStringBuilder builder, string? loginMode, string user, string password)
    {
        var mode = loginMode?.Trim() ?? string.Empty;
        if (mode.Length == 0 || mode.Equals("Windows_Login", StringComparison.OrdinalIgnoreCase))
        {
            builder.IntegratedSecurity = true;
            return;
        }

        if (mode.Equals("SQL_Server_Login", StringComparison.OrdinalIgnoreCase))
        {
            if (user.Length == 0 || password.Length == 0)
            {
                throw new InvalidOperationException(
                    "ConnectorStore__LoginMode is SQL_Server_Login. Set ConnectorStore__User and ConnectorStore__Password in .env.");
            }

            builder.IntegratedSecurity = false;
            builder.UserID = user;
            builder.Password = password;
            return;
        }

        throw new InvalidOperationException(
            "ConnectorStore__LoginMode must be Windows_Login or SQL_Server_Login.");
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
