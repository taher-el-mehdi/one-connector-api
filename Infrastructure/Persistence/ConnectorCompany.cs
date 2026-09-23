using MySqlConnector;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class ConnectorCompany
{
    public const string Name = "prodware";

    public const string Label = "Prodware";

    public const string Plan = "paid";

    public static async Task<string> RequireAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using (var named = new MySqlCommand("SELECT name FROM company WHERE name = @name LIMIT 1", connection))
        {
            named.Parameters.AddWithValue("@name", Name);
            var value = await named.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (value is not null and not DBNull)
            {
                var company = Convert.ToString(value);
                if (!string.IsNullOrEmpty(company))
                {
                    return company;
                }
            }
        }

        await using var any = new MySqlCommand("SELECT name FROM company ORDER BY name LIMIT 1", connection);
        var fallback = await any.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var name = fallback is null or DBNull ? null : Convert.ToString(fallback);
        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidOperationException("The connector store has no company.");
        }

        return name;
    }
}
