using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;

if (args.Length == 0 || !string.Equals(args[0], "add-user", StringComparison.OrdinalIgnoreCase))
{
    PrintUsage();
    return 1;
}

try
{
    var options = UserOptions.Parse(args[1..]);
    var connectionString = ConnectorStore.ConnectionString(FindEnvFile());
    var id = AddUser(connectionString, options);
    Console.WriteLine($"Added operator '{options.Username}' ({id}).");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        Add an operator account to the connector store. This command does not start the API.

        Usage:
          dotnet run --project Cli -- add-user --username <name> [--password <password>] [--display-name <name>] [--email <address>]

        Connection settings are read from .env in the current directory (ConnectorStore__Server, ConnectorStore__Database, and optional ConnectorStore__User / ConnectorStore__Password).
        Omit --password to type it without putting it in shell history.
        """);
}

static string FindEnvFile()
{
    var candidates = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), ".env"),
        Path.Combine(AppContext.BaseDirectory, ".env")
    };
    foreach (var candidate in candidates)
    {
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    throw new InvalidOperationException(
        "Could not find .env. Run this command from the docuware_sage_100_connector folder.");
}

static string AddUser(string connectionString, UserOptions options)
{
    var id = Guid.NewGuid().ToString("D");
    var now = DateTime.UtcNow;
    var hash = new PasswordHasher<object>().HashPassword(new object(), options.Password);
    using var connection = new SqlConnection(connectionString);
    connection.Open();
    using var command = new SqlCommand(
        """
        INSERT INTO [user] (id, username, password_hash, display_name, email, is_active, created_at, updated_at)
        VALUES (@id, @username, @passwordHash, @displayName, @email, 1, @now, @now)
        """,
        connection);
    command.Parameters.AddWithValue("@id", id);
    command.Parameters.AddWithValue("@username", options.Username);
    command.Parameters.AddWithValue("@passwordHash", hash);
    command.Parameters.AddWithValue("@displayName", (object?)options.DisplayName ?? DBNull.Value);
    command.Parameters.AddWithValue("@email", (object?)options.Email ?? DBNull.Value);
    command.Parameters.AddWithValue("@now", now);
    try
    {
        command.ExecuteNonQuery();
    }
    catch (SqlException exception) when (exception.Number is 2627 or 2601)
    {
        throw new InvalidOperationException($"An operator named '{options.Username}' already exists.");
    }

    return id;
}

internal sealed class UserOptions
{
    public required string Username { get; init; }

    public required string Password { get; init; }

    public string? DisplayName { get; init; }

    public string? Email { get; init; }

    public static UserOptions Parse(string[] args)
    {
        string? username = null;
        string? password = null;
        string? displayName = null;
        string? email = null;
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (key is not ("--username" or "--password" or "--display-name" or "--email"))
            {
                throw new InvalidOperationException($"Unknown argument '{key}'.");
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Missing value for {key}.");
            }

            var value = args[++i];
            switch (key)
            {
                case "--username":
                    username = value;
                    break;
                case "--password":
                    password = value;
                    break;
                case "--display-name":
                    displayName = value;
                    break;
                case "--email":
                    email = value;
                    break;
            }
        }

        username = username?.Trim() ?? string.Empty;
        if (username.Length is < 1 or > 128)
        {
            throw new InvalidOperationException("--username is required and must be at most 128 characters.");
        }

        if (string.IsNullOrEmpty(password))
        {
            password = ReadSecret("Password: ");
        }

        if (password.Length is < 8 or > 256)
        {
            throw new InvalidOperationException("The password must be 8 to 256 characters.");
        }

        displayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (displayName is { Length: > 256 })
        {
            throw new InvalidOperationException("--display-name must be at most 256 characters.");
        }

        if (email is { Length: > 256 })
        {
            throw new InvalidOperationException("--email must be at most 256 characters.");
        }

        return new UserOptions
        {
            Username = username,
            Password = password,
            DisplayName = displayName,
            Email = email
        };
    }

    private static string ReadSecret(string prompt)
    {
        Console.Write(prompt);
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace && buffer.Length > 0)
            {
                buffer.Length--;
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }
    }
}

internal static class ConnectorStore
{
    public static string ConnectionString(string envPath)
    {
        var values = ReadEnv(envPath);
        var server = Value(values, "ConnectorStore__Server");
        if (server.Length == 0)
        {
            server = Value(values, "ConnectorStore__Host");
        }

        var database = Value(values, "ConnectorStore__Database");
        if (server.Length == 0 || database.Length == 0)
        {
            throw new InvalidOperationException(
                "Set ConnectorStore__Server and ConnectorStore__Database in .env.");
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            TrustServerCertificate = true,
            ConnectTimeout = 15
        };
        var user = Value(values, "ConnectorStore__User");
        if (user.Length > 0)
        {
            builder.IntegratedSecurity = false;
            builder.UserID = user;
            builder.Password = Value(values, "ConnectorStore__Password");
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }

    private static Dictionary<string, string> ReadEnv(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }

            values[key] = value;
        }

        return values;
    }

    private static string Value(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value.Trim() : string.Empty;
}
