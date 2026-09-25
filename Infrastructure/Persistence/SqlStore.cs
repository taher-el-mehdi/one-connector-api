using Microsoft.Data.SqlClient;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class SqlStore
{
    public static bool IsDuplicateKey(SqlException exception) =>
        exception.Number is 2627 or 2601;

    public static int InsertedId(object? scalar)
    {
        if (scalar is null or DBNull)
        {
            throw new InvalidOperationException("Insert did not return an identity value.");
        }

        return Convert.ToInt32(scalar);
    }
}

internal static class SqlReader
{
    public static string GetString(this SqlDataReader reader, string column) =>
        reader.GetString(reader.GetOrdinal(column));

    public static int GetInt32(this SqlDataReader reader, string column)
    {
        // SqlDataReader.GetInt32 requires an int column. logs.status is smallint (Int16).
        var value = reader.GetValue(reader.GetOrdinal(column));
        return Convert.ToInt32(value);
    }

    public static bool GetBoolean(this SqlDataReader reader, string column) =>
        reader.GetBoolean(reader.GetOrdinal(column));

    public static DateTime GetDateTime(this SqlDataReader reader, string column) =>
        reader.GetDateTime(reader.GetOrdinal(column));
}
