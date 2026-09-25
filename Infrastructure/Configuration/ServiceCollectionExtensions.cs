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
        services.AddSingleton<IEntityMappingStore, SqlEntityMappingStore>();
        services.AddSingleton<SageMappingRelocation>();
        services.AddHostedService<SageMappingRelocationService>();
        services.AddSingleton<SageSchemaReader>();
        services.AddSingleton<SageSourceConnection>();
        services.AddSingleton<SageMappedTableReader>();
        services.AddScoped<IMappedSageToDocuWareSync, MappedSageToDocuWareSync>();
        services.AddSingleton<SageQueryCatalog>();
        services.AddSingleton<ISageRepositoryFactory, SageRepositoryFactory>();
        services.AddSingleton<ISyncTrackingStore, SqlSyncTrackingStore>();
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
        services.AddSingleton<IConfigurationCatalog, ConfigurationCatalogStore>();
        services.AddSingleton<ISynchronizationStore, SynchronizationStore>();
        services.AddSingleton<ISynchronizationExecutionStore, SynchronizationExecutionStore>();
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

    public static SyncTrackingRecordDto ToDto(this Domain.Entities.SyncTrackingRecord record)
    {
        object? file = null;
        var fileJson = record.SerializeFile();
        if (!string.IsNullOrWhiteSpace(fileJson))
        {
            file = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(fileJson);
        }

        return new()
        {
            Id = record.Id,
            SynchronizationId = record.SynchronizationId,
            Status = record.Status.ToString(),
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
            LastAttemptAt = record.LastAttemptAt,
            LastSuccessAt = record.LastSuccessAt,
            RetryCount = record.RetryCount,
            ErrorMessage = record.ErrorMessage,
            File = file
        };
    }
}
