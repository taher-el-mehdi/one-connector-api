using System.Text.Json;
using System.Text.Json.Serialization;
using DocuWareSageConnector.Infrastructure.Configuration;
using MySqlConnector;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class ConnectorStoreSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void EnsureSeeded(MySqlConnection connection, byte[] secretsKey, string contentRoot)
    {
        var company = FindCompany(connection);
        var userCount = CountUsers(connection);
        if (company is not null && userCount > 0)
        {
            return;
        }

        var seedPath = Path.Combine(contentRoot, "Database", "seed.local.json");
        if (!File.Exists(seedPath))
        {
            throw new InvalidOperationException(
                company is null
                    ? "The connector store has no company. Add Database/seed.local.json and start the API again. The file is imported once and is not committed."
                    : "The connector store has no operator account. Add an Operator section to Database/seed.local.json and start the API again.");
        }

        var document = JsonSerializer.Deserialize<SeedDocument>(File.ReadAllText(seedPath), JsonOptions)
            ?? throw new InvalidOperationException("Database/seed.local.json is empty.");

        using var transaction = connection.BeginTransaction();
        try
        {
            if (company is null)
            {
                InsertCompany(connection, transaction, secretsKey, document);
            }

            if (userCount == 0)
            {
                InsertOperator(connection, transaction, document.Operator);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static void InsertCompany(
        MySqlConnection connection,
        MySqlTransaction transaction,
        byte[] secretsKey,
        SeedDocument document)
    {
        var docuWare = document.DocuWare ?? throw new InvalidOperationException("Database/seed.local.json must include DocuWare.");
        var sage = document.Sage ?? throw new InvalidOperationException("Database/seed.local.json must include Sage.");
        var synchronization = document.Synchronization ?? throw new InvalidOperationException("Database/seed.local.json must include Synchronization.");
        docuWare.FileCabinets ??= new DocuWareFileCabinetsOptions();
        Validate(docuWare, sage, synchronization, document.Tracking ?? new TrackingOptions());

        Execute(
            connection,
            transaction,
            """
            INSERT INTO company (name, label, plan)
            VALUES (@name, @label, @plan)
            """,
            command =>
            {
                command.Parameters.AddWithValue("@name", ConnectorCompany.Name);
                command.Parameters.AddWithValue("@label", ConnectorCompany.Label);
                command.Parameters.AddWithValue("@plan", ConnectorCompany.Plan);
            });

        InsertSetting(connection, transaction, "setting_docuware", SettingKeys.PlatformUrl, docuWare.PlatformUrl);
        InsertSetting(connection, transaction, "setting_docuware", SettingKeys.Organization, docuWare.Organization ?? string.Empty);
        InsertSetting(connection, transaction, "setting_docuware", SettingKeys.AuthenticationMode, docuWare.AuthenticationMode.ToString());
        InsertSetting(connection, transaction, "setting_docuware", SettingKeys.UserName, docuWare.UserName ?? string.Empty);
        InsertSetting(connection, transaction, "setting_docuware", SettingKeys.PasswordProtected, SecretProtector.Protect(docuWare.Password, secretsKey));
        InsertSetting(connection, transaction, "setting_docuware", SettingKeys.ClientId, docuWare.ClientId ?? string.Empty);
        InsertSetting(connection, transaction, "setting_docuware", SettingKeys.ClientSecretProtected, SecretProtector.Protect(docuWare.ClientSecret, secretsKey));
        InsertSetting(connection, transaction, "setting_docuware", SettingKeys.Scope, string.IsNullOrWhiteSpace(docuWare.Scope) ? "docuware.platform openid" : docuWare.Scope);

        InsertSetting(connection, transaction, "setting_erp", SettingKeys.ServerName, sage.Server);
        InsertSetting(connection, transaction, "setting_erp", SettingKeys.DatabaseName, sage.Database);
        InsertSetting(connection, transaction, "setting_erp", SettingKeys.AuthentificationMode, sage.Authentication.ToString());
        InsertSetting(connection, transaction, "setting_erp", SettingKeys.UserName, sage.UserName ?? string.Empty);
        InsertSetting(connection, transaction, "setting_erp", SettingKeys.PasswordProtected, SecretProtector.Protect(sage.Password, secretsKey));
        InsertSetting(connection, transaction, "setting_erp", SettingKeys.TrustServerCertificate, sage.TrustServerCertificate ? "true" : "false");
        InsertSetting(connection, transaction, "setting_erp", SettingKeys.CommandTimeoutSeconds, sage.CommandTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        InsertSetting(connection, transaction, "setting_erp", SettingKeys.SuppliersOnly, sage.SuppliersOnly ? "true" : "false");
        InsertSetting(connection, transaction, "setting_erp", SettingKeys.ChartTypeZeroOnly, sage.ChartOfAccountsTypeZeroOnly ? "true" : "false");

        InsertSetting(connection, transaction, "setting_synchronization", SettingKeys.Enabled, synchronization.Enabled ? "true" : "false");
        InsertSetting(connection, transaction, "setting_synchronization", SettingKeys.IntervalSeconds, synchronization.IntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        InsertSetting(connection, transaction, "setting_synchronization", SettingKeys.MaxRetries, synchronization.MaxRetries.ToString(System.Globalization.CultureInfo.InvariantCulture));
        InsertSetting(connection, transaction, "setting_synchronization", SettingKeys.FirstRetryDelaySeconds, synchronization.FirstRetryDelaySeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        InsertSetting(connection, transaction, "setting_synchronization", SettingKeys.InsertMissingInSage, synchronization.InsertMissingInSage ? "true" : "false");
        InsertSetting(connection, transaction, "setting_synchronization", SettingKeys.ApplySageWrites, synchronization.ApplySageWrites ? "true" : "false");
        InsertSetting(connection, transaction, "setting_synchronization", SettingKeys.SageToDocuWare, synchronization.SageToDocuWare ? "true" : "false");
        InsertSetting(connection, transaction, "setting_synchronization", SettingKeys.DocuWareToSage, synchronization.DocuWareToSage ? "true" : "false");
    }

    private static void InsertSetting(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string table,
        string key,
        string? value)
    {
        Execute(
            connection,
            transaction,
            $"INSERT INTO {table} (company, `key`, `value`) VALUES (@company, @key, @value)",
            command =>
            {
                command.Parameters.AddWithValue("@company", ConnectorCompany.Name);
                command.Parameters.AddWithValue("@key", key);
                command.Parameters.AddWithValue("@value", value is null ? DBNull.Value : value);
            });
    }

    private static void InsertOperator(MySqlConnection connection, MySqlTransaction transaction, SeedOperator? op)
    {
        if (op is null || string.IsNullOrWhiteSpace(op.Username) || string.IsNullOrEmpty(op.Password))
        {
            throw new InvalidOperationException("Database/seed.local.json Operator needs Username and Password.");
        }

        var username = op.Username.Trim();
        var email = string.IsNullOrWhiteSpace(op.Email) ? null : op.Email.Trim();
        if (username.Length > 128 || op.Password.Length < 8 || op.Password.Length > 256)
        {
            throw new InvalidOperationException("Operator username must be at most 128 characters and the password 8 to 256 characters.");
        }

        if (email is { Length: > 256 })
        {
            throw new InvalidOperationException("Operator e-mail must be at most 256 characters.");
        }

        var now = DateTime.UtcNow;
        Execute(
            connection,
            transaction,
            """
            INSERT INTO `user` (id, username, password_hash, display_name, email, is_active, created_at, updated_at)
            VALUES (@id, @username, @passwordHash, @displayName, @email, 1, @now, @now)
            """,
            command =>
            {
                command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("D"));
                command.Parameters.AddWithValue("@username", username);
                command.Parameters.AddWithValue("@passwordHash", OperatorPasswordHasher.Hash(op.Password));
                command.Parameters.AddWithValue("@displayName", string.IsNullOrWhiteSpace(op.DisplayName) ? DBNull.Value : op.DisplayName.Trim());
                command.Parameters.AddWithValue("@email", email is null ? DBNull.Value : email);
                command.Parameters.AddWithValue("@now", now);
            });
    }

    private static void Validate(
        DocuWareOptions docuWare,
        SageOptions sage,
        SynchronizationOptions synchronization,
        TrackingOptions tracking)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(ConnectorConfigurationMap.From(docuWare, sage, synchronization, tracking))
            .Build();
        var validator = new ConnectorOptionsValidator(configuration);
        var failures = new List<string>();
        Collect(validator.Validate(null, docuWare), failures);
        Collect(validator.Validate(null, sage), failures);
        Collect(validator.Validate(null, synchronization), failures);
        Collect(validator.Validate(null, tracking), failures);
        if (failures.Count > 0)
        {
            throw new InvalidOperationException("Database/seed.local.json is not valid. " + string.Join(" ", failures));
        }
    }

    private static void Collect(Microsoft.Extensions.Options.ValidateOptionsResult result, List<string> failures)
    {
        if (result.Failed && result.Failures is not null)
        {
            failures.AddRange(result.Failures);
        }
    }

    private static string? FindCompany(MySqlConnection connection)
    {
        using var command = new MySqlCommand("SELECT name FROM company LIMIT 1", connection);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    private static int CountUsers(MySqlConnection connection)
    {
        using var command = new MySqlCommand("SELECT COUNT(*) FROM `user`", connection);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void Execute(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string sql,
        Action<MySqlCommand> bind)
    {
        using var command = new MySqlCommand(sql, connection, transaction);
        bind(command);
        command.ExecuteNonQuery();
    }

    private sealed class SeedDocument
    {
        public SeedOperator? Operator { get; set; }

        public DocuWareOptions? DocuWare { get; set; }

        public SageOptions? Sage { get; set; }

        public SynchronizationOptions? Synchronization { get; set; }

        public TrackingOptions? Tracking { get; set; }
    }

    private sealed class SeedOperator
    {
        public string Username { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public string? DisplayName { get; set; }

        public string? Email { get; set; }
    }
}
