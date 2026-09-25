namespace DocuWareSageConnector.Application.Mapping;

public readonly record struct MappingConfigRef(string Code, string Type)
{
    public const int CodeMaxLength = 64;

    public const int TypeMaxLength = 32;
}

public readonly record struct NormalizedMapping(
    string EntityName,
    string CabinetName,
    MappingConfigRef EntityConfig,
    MappingConfigRef CabinetConfig,
    string Code,
    string? Description);

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

    public const int CodeMaxLength = 64;

    public const int DescriptionMaxLength = 512;

    public static NormalizedMapping NormalizePair(
        string? entityName,
        string? cabinetName,
        string? entityConfigCode,
        string? entityConfigType,
        string? cabinetConfigCode,
        string? cabinetConfigType,
        string? code,
        string? description) =>
        new(
            Require(entityName, "Entity name", NameMaxLength),
            Require(cabinetName, "Cabinet name", NameMaxLength),
            RequireConfig(entityConfigCode, entityConfigType, "Sage", "Sage configuration"),
            RequireConfig(cabinetConfigCode, cabinetConfigType, "Docuware", "DocuWare configuration"),
            Require(code, "Code", CodeMaxLength),
            Optional(description, "Description", DescriptionMaxLength));

    public static MappingConfigRef RequireConfig(string? code, string? type, string expectedType, string label)
    {
        var normalizedType = Require(type, $"{label} type", MappingConfigRef.TypeMaxLength);
        if (!normalizedType.Equals(expectedType, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"{label} must be a {expectedType} configuration.");
        }

        return new MappingConfigRef(
            Require(code, $"{label} code", MappingConfigRef.CodeMaxLength),
            expectedType);
    }

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
