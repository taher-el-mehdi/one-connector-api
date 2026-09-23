using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Application.Services;
using DocuWareSageConnector.Application.UseCases;
using DocuWareSageConnector.DocuWare.Authentication;
using DocuWareSageConnector.DocuWare.Clients;
using DocuWareSageConnector.DocuWare.FileCabinets;
using DocuWareSageConnector.Infrastructure.Persistence;
using DocuWareSageConnector.Infrastructure.Synchronization;
using DocuWareSageConnector.Sage.Queries;
using DocuWareSageConnector.Sage.Repositories;
using DocuWareSageConnector.Sage.Sql;
using DocuWareSageConnector.Worker;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace DocuWareSageConnector.Infrastructure.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddConnectorServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<DocuWareOptions>, ConnectorOptionsValidator>();
        services.AddSingleton<IValidateOptions<SageOptions>, ConnectorOptionsValidator>();
        services.AddSingleton<IValidateOptions<SynchronizationOptions>, ConnectorOptionsValidator>();
        services.AddSingleton<IValidateOptions<TrackingOptions>, ConnectorOptionsValidator>();

        services.AddOptions<DocuWareOptions>()
            .Bind(configuration.GetSection(DocuWareOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<SageOptions>()
            .Bind(configuration.GetSection(SageOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<SynchronizationOptions>()
            .Bind(configuration.GetSection(SynchronizationOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<TrackingOptions>()
            .Bind(configuration.GetSection(TrackingOptions.SectionName))
            .ValidateOnStart();

        services.AddHttpClient("DocuWareIdentity");
        services.AddSingleton<IDocuWareConnectionFactory, DocuWareConnectionFactory>();
        services.AddSingleton<FileCabinetResolver>();
        services.AddSingleton<IResilienceExecutor, ResilienceExecutor>();
        services.AddSingleton<IDocuWareDocumentService, DocuWareDocumentService>();
        services.AddSingleton<ISageSqlConnectionFactory, SageSqlConnectionFactory>();
        services.AddSingleton<IEntityMappingStore, MySqlEntityMappingStore>();
        services.AddSingleton<SageMappingRelocation>();
        services.AddHostedService<SageMappingRelocationService>();
        services.AddSingleton<SageSchemaReader>();
        services.AddSingleton<SageQueryCatalog>();
        services.AddSingleton<ISageRepositoryFactory, SageRepositoryFactory>();
        services.AddSingleton<ISyncTrackingStore, MySqlSyncTrackingStore>();
        services.AddSingleton<ISyncWorkQueue, SyncWorkQueue>();
        services.AddSingleton<ISyncStatusSnapshot, SyncStatusSnapshot>();
        services.AddScoped<ISageToDocuWareSyncUseCase, SageToDocuWareSyncUseCase>();
        services.AddScoped<IDocuWareToSageSyncUseCase, DocuWareToSageSyncUseCase>();
        services.AddScoped<ISynchronizationOrchestrator, SynchronizationOrchestrator>();
        services.AddHostedService<SynchronizationWorker>();
        services.AddSingleton<IConnectorIdentity, ConnectorIdentityStore>();
        services.AddSingleton<IDocuWareSettingsStore, DocuWareSettingsStore>();
        services.AddSingleton<ISageSettingsStore, SageSettingsStore>();
        services.AddSingleton<ISynchronizationSettingsStore, SynchronizationSettingsStore>();
        services.AddSingleton<ISynchronizationStore, SynchronizationStore>();
        services.AddSingleton<ILeadStore, LeadStore>();
        services.AddAuthentication(ConnectorSessionDefaults.Scheme)
            .AddScheme<AuthenticationSchemeOptions, SessionTokenAuthenticationHandler>(
                ConnectorSessionDefaults.Scheme,
                _ => { });
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder(ConnectorSessionDefaults.Scheme)
                .RequireAuthenticatedUser()
                .Build();
        });
        return services;
    }

    public static SyncTrackingRecordDto ToDto(this Domain.Entities.SyncTrackingRecord record) =>
        new()
        {
            Id = record.Id,
            Direction = record.Direction.ToString(),
            EntityType = record.EntityType.ToString(),
            SageNumber = record.SageNumber,
            DocuWareDocumentId = record.DocuWareDocumentId,
            Status = record.Status.ToString(),
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
            LastAttemptAt = record.LastAttemptAt,
            LastSuccessAt = record.LastSuccessAt,
            RetryCount = record.RetryCount,
            LastError = record.LastError
        };
}
