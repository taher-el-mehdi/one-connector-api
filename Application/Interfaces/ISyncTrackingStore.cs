using DocuWareSageConnector.Domain.Entities;
using DocuWareSageConnector.Domain.Enums;

namespace DocuWareSageConnector.Application.Interfaces;

public interface ISyncTrackingStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<SyncTrackingRecord?> GetAsync(
        SyncDirection direction,
        EntityType entityType,
        string sageNumber,
        CancellationToken cancellationToken);

    Task<SyncTrackingRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<SyncTrackingRecord?> GetByDocumentIdAsync(
        SyncDirection direction,
        EntityType entityType,
        int documentId,
        CancellationToken cancellationToken);

    Task UpsertAsync(SyncTrackingRecord record, CancellationToken cancellationToken);

    Task<IReadOnlyList<SyncTrackingRecord>> ListAsync(
        SyncStatus? status,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SyncTrackingRecord>> ListErrorsAsync(int take, CancellationToken cancellationToken);
}
