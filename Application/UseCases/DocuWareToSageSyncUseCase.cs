using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Domain.Entities;
using DocuWareSageConnector.Domain.Enums;
using DocuWareSageConnector.Domain.Mapping;
using DocuWareSageConnector.Infrastructure.Configuration;
using DocuWareSageConnector.Infrastructure.Synchronization;
using Microsoft.Extensions.Options;

namespace DocuWareSageConnector.Application.UseCases;

public sealed class DocuWareToSageSyncUseCase : IDocuWareToSageSyncUseCase
{
    private readonly ISageRepositoryFactory _sageFactory;
    private readonly IDocuWareDocumentService _docuWare;
    private readonly ISyncTrackingStore _tracking;
    private readonly SynchronizationOptions _options;
    private readonly ILogger<DocuWareToSageSyncUseCase> _logger;

    public DocuWareToSageSyncUseCase(
        ISageRepositoryFactory sageFactory,
        IDocuWareDocumentService docuWare,
        ISyncTrackingStore tracking,
        IOptions<SynchronizationOptions> options,
        ILogger<DocuWareToSageSyncUseCase> logger)
    {
        _sageFactory = sageFactory;
        _docuWare = docuWare;
        _tracking = tracking;
        _options = options.Value;
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

        IReadOnlyList<DocuWareDocumentInfo> documents;
        if (request.DocumentId is int documentId)
        {
            var one = await _docuWare.GetByIdAsync(entityType, documentId, cancellationToken).ConfigureAwait(false);
            documents = one is null ? [] : [one];
        }
        else if (!string.IsNullOrWhiteSpace(request.SageNumber))
        {
            var one = await _docuWare.FindByKeyAsync(entityType, request.SageNumber, cancellationToken).ConfigureAwait(false);
            documents = one is null ? [] : [one];
        }
        else
        {
            documents = await _docuWare.ListDocumentsAsync(entityType, cancellationToken).ConfigureAwait(false);
        }

        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = EntityMapper.ReadKey(entityType, document.Fields);
            if (string.IsNullOrWhiteSpace(key))
            {
                skipped++;
                messages.Add($"SKIP document {document.Id}: empty key");
                continue;
            }

            var tracking = await _tracking
                .GetAsync(SyncDirection.DocuWareToSage, entityType, key, cancellationToken)
                .ConfigureAwait(false)
                ?? new SyncTrackingRecord
                {
                    Direction = SyncDirection.DocuWareToSage,
                    EntityType = entityType,
                    SageNumber = key,
                    DocuWareDocumentId = document.Id
                };

            tracking.DocuWareDocumentId = document.Id;
            tracking.Status = SyncStatus.Processing;
            tracking.LastAttemptAt = DateTimeOffset.UtcNow;
            tracking.UpdatedAt = DateTimeOffset.UtcNow;
            await _tracking.UpsertAsync(tracking, cancellationToken).ConfigureAwait(false);

            try
            {
                var mapped = EntityMapper.MapDocuWareToSage(entityType, document.Fields);
                var writable = EntityMapper.WritableSageColumns(entityType, mapped);
                var fingerprint = FieldFingerprint.Compute(writable);
                var existing = await repository.GetByKeyAsync(key, cancellationToken).ConfigureAwait(false);

                if (!request.Force
                    && existing is not null
                    && (string.Equals(tracking.Fingerprint, fingerprint, StringComparison.Ordinal)
                        || EntityMapper.WritableColumnsEqual(entityType, mapped, existing.Columns)))
                {
                    tracking.Status = SyncStatus.Skipped;
                    tracking.Fingerprint = fingerprint;
                    tracking.UpdatedAt = DateTimeOffset.UtcNow;
                    await _tracking.UpsertAsync(tracking, cancellationToken).ConfigureAwait(false);
                    skipped++;
                    _logger.LogInformation(
                        "SyncId={SyncId} Direction=DocuWareToSage Entity={Entity} SageNumber={SageNumber} DocuWareDocumentId={DocumentId} Status=Skipped",
                        syncId,
                        entityType,
                        key,
                        document.Id);
                    continue;
                }

                if (existing is not null)
                {
                    if (!_options.ApplySageWrites)
                    {
                        tracking.Status = SyncStatus.Skipped;
                        tracking.LastError = "ApplySageWrites is disabled";
                        tracking.UpdatedAt = DateTimeOffset.UtcNow;
                        await _tracking.UpsertAsync(tracking, cancellationToken).ConfigureAwait(false);
                        skipped++;
                        messages.Add($"DRY {key} ({writable.Count} columns)");
                        continue;
                    }

                    if (existing.CbMarq is null)
                    {
                        throw new InvalidOperationException($"{key}: cbMarq is missing; cannot update Sage safely.");
                    }

                    await repository.UpdateAsync(key, writable, existing.CbMarq.Value, cancellationToken)
                        .ConfigureAwait(false);
                    tracking.Status = SyncStatus.Completed;
                    tracking.Fingerprint = fingerprint;
                    tracking.LastSuccessAt = DateTimeOffset.UtcNow;
                    tracking.LastError = null;
                    tracking.RetryCount = 0;
                    updated++;
                    messages.Add($"UPDATE {key}");
                    _logger.LogInformation(
                        "SyncId={SyncId} Direction=DocuWareToSage Entity={Entity} SageNumber={SageNumber} DocuWareDocumentId={DocumentId} Status=Completed Action=Update",
                        syncId,
                        entityType,
                        key,
                        document.Id);
                }
                else if (_options.InsertMissingInSage)
                {
                    if (!_options.ApplySageWrites)
                    {
                        skipped++;
                        messages.Add($"DRY-INSERT {key}");
                        tracking.Status = SyncStatus.Skipped;
                        tracking.UpdatedAt = DateTimeOffset.UtcNow;
                        await _tracking.UpsertAsync(tracking, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    await repository.InsertAsync(mapped, cancellationToken).ConfigureAwait(false);
                    tracking.Status = SyncStatus.Completed;
                    tracking.Fingerprint = fingerprint;
                    tracking.LastSuccessAt = DateTimeOffset.UtcNow;
                    tracking.LastError = null;
                    tracking.RetryCount = 0;
                    created++;
                    messages.Add($"INSERT {key}");
                    _logger.LogInformation(
                        "SyncId={SyncId} Direction=DocuWareToSage Entity={Entity} SageNumber={SageNumber} DocuWareDocumentId={DocumentId} Status=Completed Action=Insert",
                        syncId,
                        entityType,
                        key,
                        document.Id);
                }
                else
                {
                    tracking.Status = SyncStatus.Skipped;
                    tracking.LastError = "Missing in Sage";
                    tracking.UpdatedAt = DateTimeOffset.UtcNow;
                    skipped++;
                    messages.Add($"SKIP {key}: absent from Sage");
                    _logger.LogInformation(
                        "SyncId={SyncId} Direction=DocuWareToSage Entity={Entity} SageNumber={SageNumber} DocuWareDocumentId={DocumentId} Status=Skipped Reason=MissingInSage",
                        syncId,
                        entityType,
                        key,
                        document.Id);
                }

                tracking.UpdatedAt = DateTimeOffset.UtcNow;
                await _tracking.UpsertAsync(tracking, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                var message = ex.Message;
                if (ErrorClassifier.IsSageLock(ex))
                {
                    message += " | Close Sage 100 and retry.";
                }

                tracking.Status = SyncStatus.Failed;
                tracking.LastError = message;
                tracking.RetryCount++;
                tracking.UpdatedAt = DateTimeOffset.UtcNow;
                await _tracking.UpsertAsync(tracking, cancellationToken).ConfigureAwait(false);
                _logger.LogError(
                    ex,
                    "SyncId={SyncId} Direction=DocuWareToSage Entity={Entity} SageNumber={SageNumber} DocuWareDocumentId={DocumentId} Status=Failed Permanent={Permanent}",
                    syncId,
                    entityType,
                    key,
                    document.Id,
                    ErrorClassifier.Classify(ex) == ErrorClass.Permanent);
            }
        }

        return new EntitySyncResult
        {
            EntityType = entityType,
            Direction = SyncDirection.DocuWareToSage,
            Created = created,
            Updated = updated,
            Skipped = skipped,
            Failed = failed,
            Messages = messages
        };
    }
}
