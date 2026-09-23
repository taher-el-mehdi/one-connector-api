namespace DocuWareSageConnector.Domain.Enums;

public enum SyncDirection
{
    SageToDocuWare = 1,
    DocuWareToSage = 2
}

public enum EntityType
{
    Supplier = 1,
    ChartOfAccounts = 2,
    AnalyticSection = 3
}

public enum SyncStatus
{
    Pending = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4,
    Skipped = 5
}

public enum IndexFieldType
{
    Text = 1,
    Numeric = 2,
    DateTime = 3
}

public enum DocuWareAuthenticationMode
{
    UserPassword = 1,
    AppRegistration = 2
}

public enum SageAuthenticationMode
{
    Windows = 1,
    Sql = 2
}

public enum ErrorClass
{
    Transient = 1,
    Permanent = 2
}
