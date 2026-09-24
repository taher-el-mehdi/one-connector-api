using DocuWareSageConnector.Application;
using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Domain.Enums;

namespace DocuWareSageConnector.Infrastructure.Persistence;

internal static class ConfigurationImportRules
{
    public static IReadOnlyList<ConfigurationSettingRow> Normalize(IReadOnlyList<ConfigurationSettingRow>? rows)
    {
        if (rows is null || rows.Count == 0)
        {
            throw new SettingsValidationException("import_empty");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<ConfigurationSettingRow>(rows.Count);
        foreach (var row in rows)
        {
            var type = row.Type.Trim();
            var code = row.Code.Trim();
            var key = row.Key.Trim();
            var description = string.IsNullOrWhiteSpace(row.Description) ? null : row.Description.Trim();
            var value = string.IsNullOrEmpty(row.Value) ? null : row.Value;

            if (type.Length == 0 || code.Length == 0 || key.Length == 0)
            {
                throw new SettingsValidationException("identity_required");
            }

            if (!SettingTable.IsStoredType(type))
            {
                throw new SettingsValidationException("type_invalid");
            }

            if (type.Length > 32 || code.Length > 64 || key.Length > 64
                || description is { Length: > 512 }
                || value is { Length: > 2048 })
            {
                throw new SettingsValidationException("value_too_long");
            }

            if (!seen.Add($"{type}\0{code}\0{key}"))
            {
                throw new SettingsValidationException("duplicate_row");
            }

            if (value is not null)
            {
                if (key.Equals(SettingKeys.AuthenticationMode, StringComparison.OrdinalIgnoreCase)
                    && !Enum.TryParse<DocuWareAuthenticationMode>(value, ignoreCase: true, out _))
                {
                    throw new SettingsValidationException("authentication_invalid");
                }

                if (key.Equals(SettingKeys.AuthentificationMode, StringComparison.OrdinalIgnoreCase)
                    && !Enum.TryParse<SageAuthenticationMode>(value, ignoreCase: true, out _))
                {
                    throw new SettingsValidationException("authentication_invalid");
                }
            }

            normalized.Add(new ConfigurationSettingRow
            {
                Type = type,
                Code = code,
                Description = description,
                Key = key,
                Value = value,
                Configured = row.Configured,
                Status = row.Status,
                Required = row.Required
            });
        }

        return normalized;
    }
}
