using Microsoft.Data.SqlClient;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class ConnectorStoreSeeder
{
    public static void EnsureSeeded(SqlConnection connection)
    {
        if (HasSettings(connection))
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        try
        {
            InsertGroup(connection, transaction, SettingTable.DocuWare, "DocuWare", SettingTemplates.For(SettingTable.DocuWare));
            InsertGroup(connection, transaction, SettingTable.Sage, "Sage", SettingTemplates.For(SettingTable.Sage));
            InsertGroup(connection, transaction, SettingTable.Synchronization, "Synchronization", SynchronizationDefaults);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static readonly (string Key, string? Value)[] SynchronizationDefaults =
    [
        (SettingKeys.Enabled, "false"),
        (SettingKeys.IntervalSeconds, "30"),
        (SettingKeys.MaxRetries, "3"),
        (SettingKeys.FirstRetryDelaySeconds, "2"),
        (SettingKeys.InsertMissingInSage, "false"),
        (SettingKeys.ApplySageWrites, "true"),
        (SettingKeys.SageToDocuWare, "true"),
        (SettingKeys.DocuWareToSage, "true")
    ];

    private static void InsertGroup(
        SqlConnection connection,
        SqlTransaction transaction,
        string type,
        string description,
        IReadOnlyList<(string Key, string? Value)> rows)
    {
        foreach (var (key, value) in rows)
        {
            using var command = new SqlCommand(
                """
                INSERT INTO setting (type, code, description, [key], [value])
                VALUES (@type, @code, @description, @key, @value)
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue("@type", type);
            command.Parameters.AddWithValue("@code", type);
            command.Parameters.AddWithValue("@description", description);
            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@value", value is null ? DBNull.Value : value);
            command.ExecuteNonQuery();
        }
    }

    private static bool HasSettings(SqlConnection connection)
    {
        using var command = new SqlCommand("SELECT COUNT(*) FROM setting", connection);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }
}
