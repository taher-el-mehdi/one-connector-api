using DocuWareSageConnector.Domain.Enums;

namespace DocuWareSageConnector.Application.DTOs;

public sealed class SyncCycleRequest
{
    public EntityType? EntityType { get; init; }

    public string? SageNumber { get; init; }

    public int? DocumentId { get; init; }

    public Guid? TrackingId { get; init; }

    public bool Force { get; init; }

    public int? SynchronizationId { get; init; }

    public bool? SageToDocuWare { get; init; }

    public bool? DocuWareToSage { get; init; }
}

public sealed class SyncCycleResult
{
    public Guid SyncId { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset CompletedAt { get; init; }

    public IReadOnlyList<EntitySyncResult> Entities { get; init; } = [];

    public int Created => Entities.Sum(item => item.Created);

    public int Updated => Entities.Sum(item => item.Updated);

    public int Skipped => Entities.Sum(item => item.Skipped);

    public int Failed => Entities.Sum(item => item.Failed);
}

public sealed class EntitySyncResult
{
    public required EntityType EntityType { get; init; }

    public required SyncDirection Direction { get; init; }

    public int Created { get; init; }

    public int Updated { get; init; }

    public int Skipped { get; init; }

    public int Failed { get; init; }

    public string? Error { get; init; }

    public IReadOnlyList<string> Messages { get; init; } = [];
}

public sealed class HealthResponse
{
    public required string Status { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required ComponentHealth DocuWare { get; init; }

    public required ComponentHealth Sage { get; init; }

    public required ComponentHealth Tracking { get; init; }
}

public sealed class ComponentHealth
{
    public required string Status { get; init; }

    public string? Detail { get; init; }
}

public sealed class SupplierDto
{
    public required string Number { get; init; }

    public string? Title { get; init; }

    public object? Type { get; init; }

    public string? City { get; init; }

    public string? Email { get; init; }
}

public sealed class ChartOfAccountsDto
{
    public required string Number { get; init; }

    public string? Title { get; init; }

    public object? Nature { get; init; }
}

public sealed class AnalyticSectionDto
{
    public required string Code { get; init; }

    public string? Description { get; init; }
}

public sealed class SyncStatusResponse
{
    public required bool WorkerEnabled { get; init; }

    public required int IntervalSeconds { get; init; }

    public bool CycleInProgress { get; init; }

    public DateTimeOffset? LastCycleAt { get; init; }

    public Guid? LastSyncId { get; init; }

    public SyncCycleSummaryDto? LastCycle { get; init; }

    public required IReadOnlyList<SyncTrackingRecordDto> Recent { get; init; }
}

public sealed class SyncCycleSummaryDto
{
    public required Guid SyncId { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }

    public int Created { get; init; }

    public int Updated { get; init; }

    public int Skipped { get; init; }

    public int Failed { get; init; }

    public IReadOnlyList<SyncCycleEntitySummaryDto> Entities { get; init; } = [];
}

public sealed class SyncCycleEntitySummaryDto
{
    public required string EntityType { get; init; }

    public required string Direction { get; init; }

    public int Created { get; init; }

    public int Updated { get; init; }

    public int Skipped { get; init; }

    public int Failed { get; init; }

    public string? Error { get; init; }
}

public sealed class PublicConfigurationResponse
{
    public required PublicDocuWareConfiguration DocuWare { get; init; }

    public required PublicSageConfiguration Sage { get; init; }

    public required PublicSynchronizationConfiguration Synchronization { get; init; }

    public required PublicTrackingConfiguration Tracking { get; init; }
}

public sealed class PublicDocuWareConfiguration
{
    public required string PlatformUrl { get; init; }

    public required string Organization { get; init; }

    public required string AuthenticationMode { get; init; }

    public required string UserName { get; init; }

    public required bool PasswordConfigured { get; init; }

    public required string ClientId { get; init; }

    public required bool ClientSecretConfigured { get; init; }

    public required string Scope { get; init; }

    public required bool Configured { get; init; }

    public required bool Status { get; init; }

    public required DocuWareFieldRequirements Required { get; init; }

    public required PublicFileCabinetConfiguration Supplier { get; init; }

    public required PublicFileCabinetConfiguration ChartOfAccounts { get; init; }

    public required PublicFileCabinetConfiguration AnalyticSection { get; init; }
}

public sealed class DocuWareFieldRequirements
{
    public bool PlatformUrl { get; init; }

    public bool Organization { get; init; }

    public bool Authentication { get; init; }

    public bool UserName { get; init; }

    public bool Password { get; init; }

    public bool ClientId { get; init; }

    public bool ClientSecret { get; init; }

    public bool Scope { get; init; }
}

public sealed class UpdateDocuWareConfigurationRequest
{
    public string PlatformUrl { get; set; } = string.Empty;

    public string Organization { get; set; } = string.Empty;

    public string AuthenticationMode { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string? Password { get; set; }

    public string ClientId { get; set; } = string.Empty;

    public string? ClientSecret { get; set; }

    public string Scope { get; set; } = string.Empty;
}

public sealed class SetDocuWareStatusRequest
{
    public bool Status { get; set; }
}

public sealed class RevealDocuWareSecretRequest
{
    public string Password { get; set; } = string.Empty;

    public string Field { get; set; } = string.Empty;
}

public sealed class RevealDocuWareSecretResponse
{
    public required string Value { get; init; }
}

public sealed class DocuWareCabinet
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public bool IsBasket { get; init; }
}

public sealed class DocuWareCabinetField
{
    public required string DatabaseName { get; init; }

    public required string DisplayName { get; init; }

    public required string Type { get; init; }

    public required string Scope { get; init; }

    public int Length { get; init; }

    public int Precision { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<string> Columns { get; init; } = [];
}

public sealed class DocuWareCabinetDetail
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public bool IsBasket { get; init; }

    public IReadOnlyList<DocuWareCabinetField> Fields { get; init; } = [];
}

public sealed class FileCabinetDto
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public bool IsBasket { get; init; }

    public string? UsedFor { get; init; }
}

public sealed class FileCabinetDetailDto
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public bool IsBasket { get; init; }

    public string? UsedFor { get; init; }

    public IReadOnlyList<DocuWareCabinetField> Fields { get; init; } = [];
}

public sealed class SageTableDto
{
    public required string Schema { get; init; }

    public required string Name { get; init; }

    public string? UsedFor { get; init; }
}

public sealed class SageColumnDto
{
    public required string Name { get; init; }

    public required string DataType { get; init; }

    public int? MaxLength { get; init; }

    public int? Precision { get; init; }

    public int? Scale { get; init; }

    public bool Nullable { get; init; }

    public string? DefaultValue { get; init; }
}

public sealed class SageTableDetailDto
{
    public required string Schema { get; init; }

    public required string Name { get; init; }

    public string? UsedFor { get; init; }

    public string? KeyColumn { get; init; }

    public IReadOnlyList<SageColumnDto> Fields { get; init; } = [];
}

public sealed class PublicFileCabinetConfiguration
{
    public required string Name { get; init; }

    public required string Id { get; init; }
}

public sealed class PublicSageConfiguration
{
    public required string Server { get; init; }

    public required string Database { get; init; }

    public required string Authentication { get; init; }

    public required string UserName { get; init; }

    public required bool PasswordConfigured { get; init; }

    public required bool TrustServerCertificate { get; init; }

    public required int CommandTimeoutSeconds { get; init; }

    public required bool SuppliersOnly { get; init; }

    public required bool ChartOfAccountsTypeZeroOnly { get; init; }

    public required bool Configured { get; init; }

    public required bool Status { get; init; }

    public required SageFieldRequirements Required { get; init; }
}

public sealed class SageFieldRequirements
{
    public bool Server { get; init; }

    public bool Database { get; init; }

    public bool Authentication { get; init; }

    public bool UserName { get; init; }

    public bool Password { get; init; }

    public bool CommandTimeout { get; init; }
}

public sealed class UpdateSageConfigurationRequest
{
    public string Server { get; set; } = string.Empty;

    public string Database { get; set; } = string.Empty;

    public string Authentication { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string? Password { get; set; }

    public int CommandTimeoutSeconds { get; set; } = 30;
}

public sealed class SetSageStatusRequest
{
    public bool Status { get; set; }
}

public sealed class RevealSagePasswordRequest
{
    public string Password { get; set; } = string.Empty;
}

public sealed class PublicSynchronizationConfiguration
{
    public required bool Enabled { get; init; }

    public required int IntervalSeconds { get; init; }

    public required int MaxRetries { get; init; }

    public required int FirstRetryDelaySeconds { get; init; }

    public required bool InsertMissingInSage { get; init; }

    public required bool ApplySageWrites { get; init; }

    public required bool SageToDocuWare { get; init; }

    public required bool DocuWareToSage { get; init; }

    public required bool Configured { get; init; }

    public required bool Status { get; init; }

    public required SynchronizationFieldRequirements Required { get; init; }
}

public sealed class SynchronizationFieldRequirements
{
    public bool Interval { get; init; }

    public bool MaxRetries { get; init; }

    public bool FirstRetryDelay { get; init; }
}

public sealed class UpdateSynchronizationConfigurationRequest
{
    public int IntervalSeconds { get; set; }

    public int MaxRetries { get; set; }

    public int FirstRetryDelaySeconds { get; set; }
}

public sealed class PublicTrackingConfiguration
{
    public required string DatabasePath { get; init; }
}

public sealed class SyncTrackingRecordDto
{
    public required Guid Id { get; init; }

    public required string Direction { get; init; }

    public required string EntityType { get; init; }

    public string? SageNumber { get; init; }

    public int? DocuWareDocumentId { get; init; }

    public required string Status { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? LastAttemptAt { get; init; }

    public DateTimeOffset? LastSuccessAt { get; init; }

    public int RetryCount { get; init; }

    public string? LastError { get; init; }
}

public sealed class EntityMappingListDto
{
    public bool SchemaReady { get; init; }

    public IReadOnlyList<EntityMappingDto> Mappings { get; init; } = [];
}

public sealed class EntityMappingDto
{
    public int Id { get; init; }

    public required string EntityName { get; init; }

    public required string CabinetName { get; init; }

    public IReadOnlyList<EntityMappingFieldDto> Fields { get; init; } = [];
}

public sealed class EntityMappingFieldDto
{
    public int Id { get; init; }

    public int MappingId { get; init; }

    public required string EntityFieldName { get; init; }

    public required string CabinetFieldName { get; init; }

    public string? EntityTypeName { get; init; }

    public string? CabinetTypeName { get; init; }

    public int? EntityTypeLong { get; init; }

    public int? CabinetTypeLong { get; init; }
}

public sealed class SaveEntityMappingRequest
{
    public string? EntityName { get; init; }

    public string? CabinetName { get; init; }
}

public sealed class SynchronizationListDto
{
    public IReadOnlyList<SynchronizationRecordDto> Synchronizations { get; init; } = [];
}

public sealed class SynchronizationRecordDto
{
    public int Id { get; init; }

    public required string Direction { get; init; }

    public required string Source { get; init; }

    public required string Destination { get; init; }

    public int MappingTableId { get; init; }

    public required string Code { get; init; }

    public string? Description { get; init; }

    public string? Status { get; init; }

    public int MaxRetries { get; init; }

    public int TimeoutSeconds { get; init; }

    public DateTimeOffset? NextRunAt { get; init; }

    public int RetryCount { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class SaveSynchronizationRequest
{
    public string? Direction { get; init; }

    public string? Source { get; init; }

    public string? Destination { get; init; }

    public int? MappingTableId { get; init; }

    public string? Code { get; init; }

    public string? Description { get; init; }

    public int? MaxRetries { get; init; }

    public int? TimeoutSeconds { get; init; }

    public DateTimeOffset? NextRunAt { get; init; }
}

public sealed class SaveEntityMappingFieldRequest
{
    public string? EntityFieldName { get; init; }

    public string? CabinetFieldName { get; init; }

    public string? EntityTypeName { get; init; }

    public string? CabinetTypeName { get; init; }

    public int? EntityTypeLong { get; init; }

    public int? CabinetTypeLong { get; init; }
}
