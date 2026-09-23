using DocuWareSageConnector.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DocuWareSageConnector.Sage.Sql;

public interface ISageSqlConnectionFactory
{
    SqlConnection Create();
}

public sealed class SageSqlConnectionFactory : ISageSqlConnectionFactory
{
    private readonly SageOptions _options;

    public SageSqlConnectionFactory(IOptions<SageOptions> options)
    {
        _options = options.Value;
    }

    public SqlConnection Create() => new(BuildConnectionString());

    public string BuildConnectionString()
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = _options.Server,
            InitialCatalog = _options.Database,
            TrustServerCertificate = _options.TrustServerCertificate,
            ConnectTimeout = Math.Max(_options.CommandTimeoutSeconds, 5)
        };

        if (_options.Authentication == Domain.Enums.SageAuthenticationMode.Sql)
        {
            builder.IntegratedSecurity = false;
            builder.UserID = _options.UserName;
            builder.Password = _options.Password;
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }
}
