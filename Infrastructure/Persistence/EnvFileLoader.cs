using Microsoft.Extensions.Configuration;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class EnvFileLoader
{
    public static void ApplyMissing(ConfigurationManager configuration, string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
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

            var key = line[..separator].Trim().Replace("__", ":", StringComparison.Ordinal);
            var value = Unquote(line[(separator + 1)..].Trim());
            if (string.IsNullOrEmpty(configuration[key]))
            {
                values[key] = value;
            }
        }

        if (values.Count > 0)
        {
            configuration.AddInMemoryCollection(values);
        }
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1];
        }

        return value;
    }
}
