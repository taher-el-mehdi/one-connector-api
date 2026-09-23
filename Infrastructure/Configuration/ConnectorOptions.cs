using DocuWareSageConnector.Domain.Enums;

namespace DocuWareSageConnector.Infrastructure.Configuration;

public sealed class DocuWareOptions
{
    public const string SectionName = "DocuWare";

    public string PlatformUrl { get; set; } = string.Empty;

    public string Organization { get; set; } = string.Empty;

    public DocuWareAuthenticationMode AuthenticationMode { get; set; } = DocuWareAuthenticationMode.UserPassword;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string Scope { get; set; } = "docuware.platform openid";

    public bool Configured { get; set; }

    public bool Status { get; set; }

    public DocuWareFileCabinetsOptions FileCabinets { get; set; } = new();
}

public sealed class DocuWareFileCabinetsOptions
{
    public FileCabinetOptions Supplier { get; set; } = new() { Name = "Fournisseur" };

    public FileCabinetOptions ChartOfAccounts { get; set; } = new() { Name = "Plan Comptable" };

    public FileCabinetOptions AnalyticSection { get; set; } = new() { Name = "Section analytique" };

    public FileCabinetOptions For(EntityType entityType) =>
        entityType switch
        {
            EntityType.Supplier => Supplier,
            EntityType.ChartOfAccounts => ChartOfAccounts,
            EntityType.AnalyticSection => AnalyticSection,
            _ => throw new ArgumentOutOfRangeException(nameof(entityType), entityType, "Unknown entity type.")
        };
}

public sealed class FileCabinetOptions
{
    public string Name { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;
}

public sealed class SageOptions
{
    public const string SectionName = "Sage";

    public string Server { get; set; } = string.Empty;

    public string Database { get; set; } = string.Empty;

    public SageAuthenticationMode Authentication { get; set; } = SageAuthenticationMode.Windows;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool TrustServerCertificate { get; set; } = true;

    public int CommandTimeoutSeconds { get; set; } = 30;

    public bool SuppliersOnly { get; set; } = true;

    public bool ChartOfAccountsTypeZeroOnly { get; set; } = true;

    public bool Configured { get; set; }

    public bool Status { get; set; }
}

public sealed class SynchronizationOptions
{
    public const string SectionName = "Synchronization";

    public bool Enabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = 30;

    public int MaxRetries { get; set; } = 3;

    public int FirstRetryDelaySeconds { get; set; } = 2;

    public bool InsertMissingInSage { get; set; }

    public bool ApplySageWrites { get; set; } = true;

    public bool SageToDocuWare { get; set; } = true;

    public bool DocuWareToSage { get; set; } = true;

    public bool Configured { get; set; }

    public bool Status { get; set; }
}

public sealed class TrackingOptions
{
    public const string SectionName = "Tracking";

    public string DatabasePath { get; set; } = "logs";
}
