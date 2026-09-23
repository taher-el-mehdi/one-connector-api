using System.Globalization;
using DocuWareSageConnector.Domain.Entities;
using DocuWareSageConnector.Domain.Enums;

namespace DocuWareSageConnector.Domain.Mapping;

public static class EntityMapper
{
    public static IReadOnlyList<IndexFieldValue> MapSageToDocuWare(
        EntityType entityType,
        IReadOnlyDictionary<string, object?> sageColumns)
    {
        var fields = new List<IndexFieldValue>();
        foreach (var mapping in EntityFieldMaps.For(entityType))
        {
            object? raw = null;
            if (mapping.IsComputed && mapping.DocuWareField == "NUMINTITULE")
            {
                raw = BuildNumIntitule(sageColumns);
            }
            else if (!string.IsNullOrWhiteSpace(mapping.SageColumn)
                     && sageColumns.TryGetValue(mapping.SageColumn, out var columnValue))
            {
                raw = columnValue;
            }

            var formatted = ValueConverters.FormatForDocuWare(raw, mapping.Type);
            if (formatted is null)
            {
                continue;
            }

            fields.Add(new IndexFieldValue
            {
                Name = mapping.DocuWareField,
                Type = mapping.Type,
                Value = formatted
            });
        }

        return fields;
    }

    public static IReadOnlyDictionary<string, object?> MapDocuWareToSage(
        EntityType entityType,
        IReadOnlyList<IndexFieldValue> fields)
    {
        var byName = fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Name))
            .ToDictionary(field => field.Name, field => field.Value, StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in EntityFieldMaps.For(entityType))
        {
            if (string.IsNullOrWhiteSpace(mapping.SageColumn) || mapping.IsComputed)
            {
                continue;
            }

            if (!byName.TryGetValue(mapping.DocuWareField, out var raw))
            {
                continue;
            }

            var formatted = ValueConverters.FormatForSage(raw, mapping.Type, mapping.SageColumn);
            if (formatted is null)
            {
                continue;
            }

            result[mapping.SageColumn] = formatted;
        }

        return result;
    }

    public static IReadOnlyDictionary<string, object?> WritableSageColumns(
        EntityType entityType,
        IReadOnlyDictionary<string, object?> sageColumns)
    {
        var skip = EntityFieldMaps.SageWriteSkipColumns(entityType);
        return sageColumns
            .Where(pair => !skip.Contains(pair.Key) && pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    public static bool WritableColumnsEqual(
        EntityType entityType,
        IReadOnlyDictionary<string, object?> incoming,
        IReadOnlyDictionary<string, object?> existing)
    {
        var writable = WritableSageColumns(entityType, incoming);
        foreach (var mapping in EntityFieldMaps.For(entityType))
        {
            if (mapping.SkipSageWrite || string.IsNullOrWhiteSpace(mapping.SageColumn) || mapping.IsComputed)
            {
                continue;
            }

            incoming.TryGetValue(mapping.SageColumn, out var left);
            existing.TryGetValue(mapping.SageColumn, out var right);
            if (!writable.ContainsKey(mapping.SageColumn) && right is null)
            {
                continue;
            }

            if (!ValueConverters.AreEquivalent(left, right, mapping.Type))
            {
                return false;
            }
        }

        return true;
    }

    public static IReadOnlyDictionary<string, object?> ToFingerprintDictionary(IReadOnlyList<IndexFieldValue> fields) =>
        fields.ToDictionary(field => field.Name, field => field.Value, StringComparer.OrdinalIgnoreCase);

    public static string? ReadKey(EntityType entityType, IReadOnlyList<IndexFieldValue> fields)
    {
        var key = EntityFieldMaps.KeyOf(entityType);
        var match = fields.FirstOrDefault(field =>
            string.Equals(field.Name, key.DocuWareField, StringComparison.OrdinalIgnoreCase));
        return ValueConverters.ToText(match?.Value);
    }

    public static string? ReadSageKey(EntityType entityType, IReadOnlyDictionary<string, object?> columns)
    {
        var key = EntityFieldMaps.KeyOf(entityType);
        if (string.IsNullOrWhiteSpace(key.SageColumn) || !columns.TryGetValue(key.SageColumn, out var value))
        {
            return null;
        }

        return ValueConverters.ToText(value);
    }

    private static string? BuildNumIntitule(IReadOnlyDictionary<string, object?> sageColumns)
    {
        sageColumns.TryGetValue("CG_Num", out var num);
        sageColumns.TryGetValue("CG_Intitule", out var intitule);
        var numText = ValueConverters.ToText(num) ?? Convert.ToString(num, CultureInfo.InvariantCulture)?.Trim();
        var intituleText = ValueConverters.ToText(intitule);
        if (string.IsNullOrEmpty(numText) && string.IsNullOrEmpty(intituleText))
        {
            return null;
        }

        return $"{numText}-{intituleText}";
    }
}
