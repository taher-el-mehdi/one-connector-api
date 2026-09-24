using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class ConnectorStoreBootstrap
{
    public static void Load(ConfigurationManager configuration, string contentRoot)
    {
        var connectionString = ConnectorStoreConnections.RequireConnectionString(configuration);
        var secretsKey = SecretProtector.ReadKey(configuration["ConnectorStore:SecretsKey"]);
        var host = configuration["ConnectorStore:Host"];
        var database = configuration["ConnectorStore:Database"];

        try
        {
            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            ApplySchema(connection, contentRoot);
            CompanySchema.Remove(connection);
            MappingFieldSchema.Ensure(connection);
            SynchronizationSchema.Ensure(connection);
            LogsSchema.Ensure(connection);
            SettingSchema.MigrateLegacy(connection);
            ConnectorStoreReshape.Apply(connection);
            ConnectorStoreSeeder.EnsureSeeded(connection, secretsKey, contentRoot);
            DocuWareSettingsSchema.Ensure(connection);
            configuration.AddInMemoryCollection(ConnectorSettingsReader.Read(connection, secretsKey));
        }
        catch (Exception exception) when (exception is MySqlException or CryptographicException)
        {
            throw new InvalidOperationException(
                $"Could not open the connector store at {host}/{database}. Confirm ConnectorStore__Host and that this computer is allowed to connect. {exception.Message}",
                exception);
        }
    }

    private static void ApplySchema(MySqlConnection connection, string contentRoot)
    {
        var directory = Path.Combine(contentRoot, "Database", "Migrations");
        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException($"Connector store migrations were not found at {directory}.");
        }

        foreach (var file in Directory.GetFiles(directory, "*.sql").OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            foreach (var statement in SplitStatements(File.ReadAllText(file)))
            {
                using var command = new MySqlCommand(statement, connection) { CommandTimeout = 60 };
                command.ExecuteNonQuery();
            }
        }
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
