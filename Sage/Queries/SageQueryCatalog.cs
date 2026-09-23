using DocuWareSageConnector.Domain.Enums;
using DocuWareSageConnector.Domain.Mapping;
using DocuWareSageConnector.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DocuWareSageConnector.Sage.Queries;

public sealed class SageQueryDefinition
{
    public required EntityType EntityType { get; init; }

    public required string TableName { get; init; }

    public required string KeyColumn { get; init; }

    public required IReadOnlyList<string> SelectColumns { get; init; }

    public string? FilterSql { get; init; }

    public IReadOnlyDictionary<string, object>? FilterParameters { get; init; }

    public IReadOnlyDictionary<string, object?> InsertDefaults { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
}

public sealed class SageQueryCatalog
{
    private static readonly HashSet<string> AllowedTables =
        new(StringComparer.OrdinalIgnoreCase) { "F_COMPTET", "F_COMPTEG", "F_COMPTEA" };

    private static readonly HashSet<string> AllowedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "CT_Num", "CT_Intitule", "CT_Type", "CG_NumPrinc", "CT_Qualite", "CT_Classement",
        "CT_Contact", "CT_Adresse", "CT_Complement", "CT_CodePostal", "CT_Ville", "CT_Pays",
        "CT_Ape", "CT_Identifiant", "CT_Statistique01", "CT_EMail", "CT_Telephone", "CA_Num",
        "cbModification", "cbCreation", "cbMarq",
        "CG_Num", "CG_Intitule", "N_Nature", "CG_Type",
        "CA_Intitule"
    };

    private readonly SageOptions _options;

    public SageQueryCatalog(IOptions<SageOptions> options)
    {
        _options = options.Value;
    }

    public SageQueryDefinition For(EntityType entityType) =>
        entityType switch
        {
            EntityType.Supplier => Create(
                EntityType.Supplier,
                "F_COMPTET",
                "CT_Num",
                _options.SuppliersOnly ? "CT_Type = @filterType" : null,
                _options.SuppliersOnly ? new Dictionary<string, object> { ["filterType"] = 1 } : null,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["CT_Type"] = 1 }),
            EntityType.ChartOfAccounts => Create(
                EntityType.ChartOfAccounts,
                "F_COMPTEG",
                "CG_Num",
                _options.ChartOfAccountsTypeZeroOnly ? "CG_Type = @filterType" : null,
                _options.ChartOfAccountsTypeZeroOnly ? new Dictionary<string, object> { ["filterType"] = 0 } : null,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["CG_Type"] = 0 }),
            EntityType.AnalyticSection => Create(
                EntityType.AnalyticSection,
                "F_COMPTEA",
                "CA_Num",
                null,
                null,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)),
            _ => throw new ArgumentOutOfRangeException(nameof(entityType), entityType, "Unknown entity type.")
        };

    public static void EnsureAllowedTable(string tableName)
    {
        if (!AllowedTables.Contains(tableName))
        {
            throw new InvalidOperationException($"Table '{tableName}' is not allowed.");
        }
    }

    public static void EnsureAllowedColumn(string columnName)
    {
        if (!AllowedColumns.Contains(columnName))
        {
            throw new InvalidOperationException($"Column '{columnName}' is not allowed.");
        }
    }

    private SageQueryDefinition Create(
        EntityType entityType,
        string table,
        string keyColumn,
        string? filterSql,
        Dictionary<string, object>? filterParameters,
        Dictionary<string, object?> insertDefaults)
    {
        EnsureAllowedTable(table);
        EnsureAllowedColumn(keyColumn);
        var columns = EntityFieldMaps.SageColumns(entityType).ToList();
        if (!columns.Contains("cbMarq", StringComparer.OrdinalIgnoreCase))
        {
            columns.Add("cbMarq");
        }

        foreach (var column in columns)
        {
            EnsureAllowedColumn(column);
        }

        return new SageQueryDefinition
        {
            EntityType = entityType,
            TableName = table,
            KeyColumn = keyColumn,
            SelectColumns = columns,
            FilterSql = filterSql,
            FilterParameters = filterParameters,
            InsertDefaults = insertDefaults
        };
    }
}
