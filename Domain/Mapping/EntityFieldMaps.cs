using DocuWareSageConnector.Domain.Enums;

namespace DocuWareSageConnector.Domain.Mapping;

public static class EntityFieldMaps
{
    public static IReadOnlyList<FieldMapping> Supplier { get; } =
    [
        Map("NUM", "CT_Num", IndexFieldType.Text, isKey: true, skipSageWrite: true),
        Map("INTITULE", "CT_Intitule", IndexFieldType.Text),
        Map("TYPE", "CT_Type", IndexFieldType.Numeric, skipSageWrite: true),
        Map("CG_NUMPRINC", "CG_NumPrinc", IndexFieldType.Numeric),
        Map("QUALITE", "CT_Qualite", IndexFieldType.Text),
        Map("CLASSEMENT", "CT_Classement", IndexFieldType.Text),
        Map("CONTACT", "CT_Contact", IndexFieldType.Text),
        Map("ADRESSE", "CT_Adresse", IndexFieldType.Text),
        Map("COMPLEMENT", "CT_Complement", IndexFieldType.Text),
        Map("CODEPOSTAL", "CT_CodePostal", IndexFieldType.Text),
        Map("VILLE", "CT_Ville", IndexFieldType.Text),
        Map("PAYS", "CT_Pays", IndexFieldType.Text),
        Map("APE", "CT_Ape", IndexFieldType.Text),
        Map("IDENTIFIANT", "CT_Identifiant", IndexFieldType.Text),
        Map("STATISTIQUE01", "CT_Statistique01", IndexFieldType.Text),
        Map("EMAIL", "CT_EMail", IndexFieldType.Text),
        Map("TELEPHONE", "CT_Telephone", IndexFieldType.Text),
        Map("CA_NUM", "CA_Num", IndexFieldType.Text),
        Map("CBMODIFICATION", "cbModification", IndexFieldType.DateTime, skipSageWrite: true),
        Map("CBCREATION", "cbCreation", IndexFieldType.DateTime, skipSageWrite: true)
    ];

    public static IReadOnlyList<FieldMapping> ChartOfAccounts { get; } =
    [
        Map("CG_NUM", "CG_Num", IndexFieldType.Numeric, isKey: true, skipSageWrite: true),
        Map("CG_INTITULE", "CG_Intitule", IndexFieldType.Text),
        Map("N_NATURE", "N_Nature", IndexFieldType.Numeric),
        new FieldMapping
        {
            DocuWareField = "NUMINTITULE",
            SageColumn = null,
            Type = IndexFieldType.Text,
            IsComputed = true,
            SkipSageWrite = true
        },
        Map("CBMODIFICATION", "cbModification", IndexFieldType.DateTime, skipSageWrite: true),
        Map("CBCREATION", "cbCreation", IndexFieldType.DateTime, skipSageWrite: true)
    ];

    public static IReadOnlyList<FieldMapping> AnalyticSection { get; } =
    [
        Map("CODE", "CA_Num", IndexFieldType.Text, isKey: true, skipSageWrite: true),
        Map("DESCRIPTION", "CA_Intitule", IndexFieldType.Text),
        Map("CBMODIFICATION", "cbModification", IndexFieldType.DateTime, skipSageWrite: true),
        Map("CBCREATION", "cbCreation", IndexFieldType.DateTime, skipSageWrite: true)
    ];

    public static IReadOnlyList<FieldMapping> For(EntityType entityType) =>
        entityType switch
        {
            EntityType.Supplier => Supplier,
            EntityType.ChartOfAccounts => ChartOfAccounts,
            EntityType.AnalyticSection => AnalyticSection,
            _ => throw new ArgumentOutOfRangeException(nameof(entityType), entityType, "Unknown entity type.")
        };

    public static FieldMapping KeyOf(EntityType entityType) =>
        For(entityType).Single(field => field.IsKey);

    public static IReadOnlyList<string> SageColumns(EntityType entityType) =>
        For(entityType)
            .Where(field => !string.IsNullOrWhiteSpace(field.SageColumn) && !field.IsComputed)
            .Select(field => field.SageColumn!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlySet<string> SageWriteSkipColumns(EntityType entityType) =>
        For(entityType)
            .Where(field => field.SkipSageWrite && !string.IsNullOrWhiteSpace(field.SageColumn))
            .Select(field => field.SageColumn!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static FieldMapping Map(
        string docuWareField,
        string sageColumn,
        IndexFieldType type,
        bool isKey = false,
        bool skipSageWrite = false) =>
        new()
        {
            DocuWareField = docuWareField,
            SageColumn = sageColumn,
            Type = type,
            IsKey = isKey,
            SkipSageWrite = skipSageWrite
        };
}
