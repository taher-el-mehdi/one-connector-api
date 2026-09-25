using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class ConnectorStoreBootstrap
{
    public static void Load(ConfigurationManager configuration, string contentRoot)
    {
        var connectionString = ConnectorStoreConnections.RequireConnectionString(configuration);
        var secretsKey = SecretProtector.ReadKey(configuration["ConnectorStore:SecretsKey"]);
        var server = configuration["ConnectorStore:Server"] ?? configuration["ConnectorStore:Host"];
        var database = configuration["ConnectorStore:Database"];

        try
        {
            using var connection = new SqlConnection(connectionString);
            connection.Open();
            ApplySchema(connection, contentRoot);
            CompanySchema.Remove(connection);
            MappingFieldSchema.Ensure(connection);
            SynchronizationSchema.Ensure(connection);
            SynchronizationExecutionSchema.Ensure(connection);
            LogsSchema.Ensure(connection);
            SettingSchema.MigrateLegacy(connection);
            ConnectorStoreReshape.Apply(connection);
            ConnectorStoreSeeder.EnsureSeeded(connection);
            MappingTableSchema.Ensure(connection);
            DocuWareSettingsSchema.Ensure(connection);
            configuration.AddInMemoryCollection(ConnectorSettingsReader.Read(connection, secretsKey));
        }
        catch (Exception exception) when (exception is SqlException or CryptographicException)
        {
            throw new InvalidOperationException(
                $"Could not open the connector store at {server}/{database}. Confirm ConnectorStore__Server and that this computer is allowed to connect. {exception.Message}",
                exception);
        }
    }

    private static void ApplySchema(SqlConnection connection, string contentRoot)
    {
        if (TableExists(connection, "setting"))
        {
            return;
        }

        var path = Path.Combine(contentRoot, "Database", "schema.sql");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Connector store schema was not found at {path}.");
        }

        foreach (var statement in SplitStatements(File.ReadAllText(path)))
        {
            using var command = new SqlCommand(statement, connection) { CommandTimeout = 60 };
            command.ExecuteNonQuery();
        }
    }

    private static bool TableExists(SqlConnection connection, string table)
    {
        using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_CATALOG = DB_NAME() AND TABLE_NAME = @table
            """,
            connection);
        command.Parameters.AddWithValue("@table", table);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    internal static IReadOnlyList<string> SplitStatements(string sql)
    {
        var withoutComments = new StringBuilder();
        foreach (var raw in sql.Split('\n'))
        {
            if (raw.Trim().StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            withoutComments.AppendLine(raw);
        }

        return withoutComments
            .ToString()
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
