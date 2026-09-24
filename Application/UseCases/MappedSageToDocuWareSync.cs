using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Domain.Entities;
using DocuWareSageConnector.Domain.Enums;
using DocuWareSageConnector.Domain.Mapping;
using DocuWareSageConnector.Infrastructure.Synchronization;
using DocuWareSageConnector.Sage.Sql;

namespace DocuWareSageConnector.Application.UseCases;

public interface IMappedSageToDocuWareSync
{
    Task<EntitySyncResult> ExecuteAsync(int synchronizationId, Guid syncId, bool force, CancellationToken cancellationToken);
}

public sealed class MappedSageToDocuWareSync : IMappedSageToDocuWareSync
{
    private readonly ISynchronizationStore _synchronizations;
    private readonly IEntityMappingStore _mappings;
    private readonly SageMappedTableReader _sage;
    private readonly IDocuWareDocumentService _docuWare;
    private readonly ISyncTrackingStore _tracking;
    private readonly ILogger<MappedSageToDocuWareSync> _logger;

    public MappedSageToDocuWareSync(
        ISynchronizationStore synchronizations,
        IEntityMappingStore mappings,
        SageMappedTableReader sage,
        IDocuWareDocumentService docuWare,
        ISyncTrackingStore tracking,
        ILogger<MappedSageToDocuWareSync> logger)
    {
        _synchronizations = synchronizations;
        _mappings = mappings;
        _sage = sage;
        _docuWare = docuWare;
        _tracking = tracking;
        _logger = logger;
    }

    public async Task<EntitySyncResult> ExecuteAsync(
        int synchronizationId,
        Guid syncId,
        bool force,
        CancellationToken cancellationToken)
    {
        var job = await _synchronizations.GetAsync(synchronizationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Synchronization {synchronizationId} was not found.");
        var mapping = await _mappings.GetAsync(job.MappingTableId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Mapping {job.MappingTableId} was not found.");
        if (mapping.Fields.Count == 0)
        {
            throw new InvalidOperationException($"Mapping '{mapping.EntityName}' has no fields.");
        }

        var keyField = mapping.Fields[0];
        var rows = await _sage.ReadAsync(
            mapping.EntityName,
            mapping.Fields.Select(field => field.EntityFieldName).ToArray(),
            cancellationToken).ConfigureAwait(false);

        var created = 0;
        var updated = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = ValueConverters.ToText(row.GetValueOrDefault(keyField.EntityFieldName));
            if (string.IsNullOrWhiteSpace(key))
            {
                skipped++;
                continue;
            }

            var fields = MapFields(mapping.Fields, row);
            var fingerprint = FieldFingerprint.Compute(EntityMapper.ToFingerprintDictionary(fields));
            var tracking = await _tracking
                .GetForSynchronizationAsync(synchronizationId, key, cancellationToken)
                .ConfigureAwait(false)
                ?? new SyncTrackingRecord
                {
                    Direction = SyncDirection.SageToDocuWare,
                    EntityType = EntityType.Supplier,
                    SageNumber = key,
                    SynchronizationId = synchronizationId
                };
            tracking.BindSynchronization(synchronizationId);
            tracking.SageNumber = key;
            tracking.Status = SyncStatus.Processing;
            tracking.LastAttemptAt = DateTimeOffset.UtcNow;
            tracking.UpdatedAt = DateTimeOffset.UtcNow;
            await _tracking.UpsertAsync(tracking, cancellationToken).ConfigureAwait(false);

            try
            {
                var existing = tracking.DocuWareDocumentId is int knownId
                    ? new DocuWareDocumentInfo { Id = knownId, Fields = [] }
                    : await _docuWare.FindInCabinetAsync(mapping.CabinetName, keyField.CabinetFieldName, key, cancellationToken)
                        .ConfigureAwait(false);
                if (!force && existing is not null && string.Equals(tracking.Fingerprint, fingerprint, StringComparison.Ordinal))
                {
                    tracking.Status = SyncStatus.Skipped;
                    tracking.DocuWareDocumentId = existing.Id;
                    tracking.ErrorMessage = null;
                    tracking.UpdatedAt = DateTimeOffset.UtcNow;
                    await _tracking.UpsertAsync(tracking, cancellationToken).ConfigureAwait(false);
                    skipped++;
                    continue;
                }

                if (existing is not null)
                {
                    await _docuWare.UpdateCabinetFieldsAsync(mapping.CabinetName, existing.Id, fields, cancellationToken)
                        .ConfigureAwait(false);
                    tracking.DocuWareDocumentId = existing.Id;
                    updated++;
                }
                else
                {
                    var fileName = Sanitize($"{mapping.EntityName}_{key}.json");
                    var documentId = await _docuWare.CreateInCabinetAsync(mapping.CabinetName, fileName, fields, cancellationToken)
                        .ConfigureAwait(false);
                    tracking.DocuWareDocumentId = documentId;
                    created++;
                }

                tracking.Status = SyncStatus.Completed;
                tracking.Fingerprint = fingerprint;
                tracking.LastSuccessAt = DateTimeOffset.UtcNow;
                tracking.ErrorMessage = null;
                tracking.RetryCount = 0;
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
                _logger.LogError(ex, "SyncId={SyncId} SynchronizationId={SynchronizationId} Key={Key} Status=Failed", syncId, synchronizationId, key);
            }
        }

        return new EntitySyncResult
        {
            EntityType = EntityType.Supplier,
            Direction = SyncDirection.SageToDocuWare,
            Created = created,
            Updated = updated,
            Skipped = skipped,
            Failed = failed
        };
    }

    private static IReadOnlyList<IndexFieldValue> MapFields(
        IReadOnlyList<EntityMappingFieldDto> fields,
        IReadOnlyDictionary<string, object?> row)
    {
        var mapped = new List<IndexFieldValue>();
        foreach (var field in fields)
        {
            row.TryGetValue(field.EntityFieldName, out var raw);
            var type = FieldType(field.CabinetTypeName);
            var formatted = ValueConverters.FormatForDocuWare(raw, type);
            if (formatted is null)
            {
                continue;
            }

            mapped.Add(new IndexFieldValue
            {
                Name = field.CabinetFieldName,
                Type = type,
                Value = formatted
            });
        }

        return mapped;
    }

    private static IndexFieldType FieldType(string? cabinetTypeName)
    {
        var name = cabinetTypeName?.Trim() ?? string.Empty;
        if (name.Contains("date", StringComparison.OrdinalIgnoreCase) || name.Contains("time", StringComparison.OrdinalIgnoreCase))
        {
            return IndexFieldType.DateTime;
        }

        if (name.Contains("num", StringComparison.OrdinalIgnoreCase)
            || name.Contains("int", StringComparison.OrdinalIgnoreCase)
            || name.Contains("decimal", StringComparison.OrdinalIgnoreCase))
        {
            return IndexFieldType.Numeric;
        }

        return IndexFieldType.Text;
    }

    private static string Sanitize(string fileName)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '_');
        }

        return fileName;
    }
}
