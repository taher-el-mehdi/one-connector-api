using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Domain.Entities;
using DocuWareSageConnector.Domain.Enums;
using DocuWareSageConnector.Domain.Mapping;
using DocuWareSageConnector.Infrastructure.Synchronization;

namespace DocuWareSageConnector.Application.UseCases;

public sealed class SageToDocuWareSyncUseCase : ISageToDocuWareSyncUseCase
{
    private readonly ISageRepositoryFactory _sageFactory;
    private readonly IDocuWareDocumentService _docuWare;
    private readonly ISyncTrackingStore _tracking;
    private readonly ILogger<SageToDocuWareSyncUseCase> _logger;

    public SageToDocuWareSyncUseCase(
        ISageRepositoryFactory sageFactory,
        IDocuWareDocumentService docuWare,
        ISyncTrackingStore tracking,
        ILogger<SageToDocuWareSyncUseCase> logger)
    {
        _sageFactory = sageFactory;
        _docuWare = docuWare;
        _tracking = tracking;
        _logger = logger;
    }

    public async Task<EntitySyncResult> ExecuteAsync(
        EntityType entityType,
        SyncCycleRequest request,
        Guid syncId,
        CancellationToken cancellationToken)
    {
        var created = 0;
        var updated = 0;
        var skipped = 0;
        var failed = 0;
        var messages = new List<string>();
        var repository = _sageFactory.Get(entityType);

        IReadOnlyList<SageEntityRecord> rows;
        if (!string.IsNullOrWhiteSpace(request.SageNumber))
        {
            var one = await repository.GetByKeyAsync(request.SageNumber, cancellationToken).ConfigureAwait(false);
            rows = one is null ? [] : [one];
        }
        else
        {
            rows = await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(row.Key))
            {
                skipped++;
                continue;
            }

            var tracking = await _tracking
                .GetAsync(SyncDirection.SageToDocuWare, entityType, row.Key, cancellationToken)
                .ConfigureAwait(false)
                ?? new SyncTrackingRecord
                {
                    Direction = SyncDirection.SageToDocuWare,
                    EntityType = entityType,
                    SageNumber = row.Key
                };
            tracking.BindSynchronization(request.SynchronizationId);

            tracking.Status = SyncStatus.Processing;
            tracking.LastAttemptAt = DateTimeOffset.UtcNow;
            tracking.UpdatedAt = DateTimeOffset.UtcNow;
            await _tracking.UpsertAsync(tracking, cancellationToken).ConfigureAwait(false);

            try
            {
                var fields = EntityMapper.MapSageToDocuWare(entityType, row.Columns);
                var fingerprint = FieldFingerprint.Compute(EntityMapper.ToFingerprintDictionary(fields));
                var existing = tracking.DocuWareDocumentId is int knownId
                    ? await _docuWare.GetByIdAsync(entityType, knownId, cancellationToken).ConfigureAwait(false)
                    : await _docuWare.FindByKeyAsync(entityType, row.Key, cancellationToken).ConfigureAwait(false);

                if (!request.Force
                    && existing is not null
                    && string.Equals(tracking.Fingerprint, fingerprint, StringComparison.Ordinal))
                {
                    tracking.Status = SyncStatus.Skipped;
                    tracking.DocuWareDocumentId = existing.Id;
                    tracking.ErrorMessage = null;
                    tracking.UpdatedAt = DateTimeOffset.UtcNow;
                    await _tracking.UpsertAsync(tracking, cancellationToken).ConfigureAwait(false);
                    skipped++;
                    _logger.LogInformation(
                        "SyncId={SyncId} SynchronizationId={SynchronizationId} Direction=SageToDocuWare Entity={Entity} SageNumber={SageNumber} DocuWareDocumentId={DocumentId} Status=Skipped",
                        syncId,
                        request.SynchronizationId,
                        entityType,
                        row.Key,
                        existing.Id);
                    continue;
                }

                if (existing is not null)
                {
                    await _docuWare.UpdateIndexFieldsAsync(entityType, existing.Id, fields, cancellationToken)
                        .ConfigureAwait(false);
                    tracking.DocuWareDocumentId = existing.Id;
                    tracking.Status = SyncStatus.Completed;
                    tracking.Fingerprint = fingerprint;
                    tracking.LastSuccessAt = DateTimeOffset.UtcNow;
                    tracking.ErrorMessage = null;
                    tracking.RetryCount = 0;
                    updated++;
                    messages.Add($"UPDATE {row.Key} -> {existing.Id}");
                    _logger.LogInformation(
                        "SyncId={SyncId} SynchronizationId={SynchronizationId} Direction=SageToDocuWare Entity={Entity} SageNumber={SageNumber} DocuWareDocumentId={DocumentId} Status=Completed Action=Update",
                        syncId,
                        request.SynchronizationId,
                        entityType,
                        row.Key,
                        existing.Id);
                }
                else
                {
                    var fileName = $"{entityType}_{row.Key}.json";
                    var documentId = await _docuWare
                        .CreateDocumentAsync(entityType, SanitizeFileName(fileName), fields, cancellationToken)
                        .ConfigureAwait(false);
                    tracking.DocuWareDocumentId = documentId;
                    tracking.Status = SyncStatus.Completed;
                    tracking.Fingerprint = fingerprint;
                    tracking.LastSuccessAt = DateTimeOffset.UtcNow;
                    tracking.ErrorMessage = null;
                    tracking.RetryCount = 0;
                    created++;
                    messages.Add($"CREATE {row.Key} -> {documentId}");
                    _logger.LogInformation(
                        "SyncId={SyncId} SynchronizationId={SynchronizationId} Direction=SageToDocuWare Entity={Entity} SageNumber={SageNumber} DocuWareDocumentId={DocumentId} Status=Completed Action=Create",
                        syncId,
                        request.SynchronizationId,
                        entityType,
                        row.Key,
                        documentId);
                }

                tracking.UpdatedAt = DateTimeOffset.UtcNow;
                await _tracking.UpsertAsync(tracking, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                tracking.Status = SyncStatus.Failed;
                tracking.ErrorMessage = ex.Message;
                tracking.RetryCount++;
                tracking.UpdatedAt = DateTimeOffset.UtcNow;
                await _tracking.UpsertAsync(tracking, cancellationToken).ConfigureAwait(false);
                _logger.LogError(
                    ex,
                    "SyncId={SyncId} SynchronizationId={SynchronizationId} Direction=SageToDocuWare Entity={Entity} SageNumber={SageNumber} Status=Failed Permanent={Permanent}",
                    syncId,
                    request.SynchronizationId,
                    entityType,
                    row.Key,
                    ErrorClassifier.Classify(ex) == ErrorClass.Permanent);
            }
        }

        return new EntitySyncResult
        {
            EntityType = entityType,
            Direction = SyncDirection.SageToDocuWare,
            Created = created,
            Updated = updated,
            Skipped = skipped,
            Failed = failed,
            Messages = messages
        };
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '_');
        }

        return fileName;
    }
}
