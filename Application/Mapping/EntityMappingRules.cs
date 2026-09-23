namespace DocuWareSageConnector.Application.Mapping;

public readonly record struct NormalizedMapping(string EntityName, string CabinetName);

public readonly record struct NormalizedMappingField(
    string EntityFieldName,
    string CabinetFieldName,
    string? EntityTypeName,
    string? CabinetTypeName,
    int? EntityTypeLong,
    int? CabinetTypeLong);

public static class EntityMappingRules
{
    public const int NameMaxLength = 256;

    public const int TypeNameMaxLength = 128;

    public static NormalizedMapping NormalizePair(string? entityName, string? cabinetName) =>
        new(Require(entityName, "Entity name", NameMaxLength), Require(cabinetName, "Cabinet name", NameMaxLength));

    public static NormalizedMappingField NormalizeField(
        string? entityFieldName,
        string? cabinetFieldName,
        string? entityTypeName,
        string? cabinetTypeName,
        int? entityTypeLong,
        int? cabinetTypeLong) =>
        new(
            Require(entityFieldName, "Entity field name", NameMaxLength),
            Require(cabinetFieldName, "Cabinet field name", NameMaxLength),
            Optional(entityTypeName, "Entity type name", TypeNameMaxLength),
            Optional(cabinetTypeName, "Cabinet type name", TypeNameMaxLength),
            Length(entityTypeLong, "Entity type length"),
            Length(cabinetTypeLong, "Cabinet type length"));

    private static string Require(string? value, string label, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new ArgumentException($"{label} is required.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"{label} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? Optional(string? value, string label, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"{label} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static int? Length(int? value, string label)
    {
        if (value is null)
        {
            return null;
        }

        if (value < -1)
        {
            throw new ArgumentException($"{label} must be a whole number greater than or equal to -1.");
        }

        return value;
    }
}
