using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Application.Synchronization;
using DocuWareSageConnector.Domain.Entities;
using DocuWareSageConnector.Domain.Enums;
using DocuWareSageConnector.Domain.Mapping;
using DocuWareSageConnector.Infrastructure.Synchronization;
using DocuWareSageConnector.Sage.Sql;

namespace DocuWareSageConnector.Application.UseCases;

public interface IMappedSageToDocuWareSync
{
    Task<EntitySyncResult> ExecuteAsync(
        int synchronizationId,
        Guid syncId,
        bool force,
        Guid? runId,
        CancellationToken cancellationToken);
}

public sealed class MappedSageToDocuWareSync : IMappedSageToDocuWareSync
{
    private readonly ISynchronizationStore _synchronizations;
    private readonly IEntityMappingStore _mappings;
    private readonly SageSourceConnection _sources;
    private readonly SageMappedTableReader _sage;
    private readonly IDocuWareDocumentService _docuWare;
    private readonly ISyncTrackingStore _tracking;
    private readonly ISynchronizationExecutionStore _records;
    private readonly ILogger<MappedSageToDocuWareSync> _logger;

    public MappedSageToDocuWareSync(
        ISynchronizationStore synchronizations,
        IEntityMappingStore mappings,
        SageSourceConnection sources,
        SageMappedTableReader sage,
        IDocuWareDocumentService docuWare,
        ISyncTrackingStore tracking,
        ISynchronizationExecutionStore records,
        ILogger<MappedSageToDocuWareSync> logger)
    {
        _synchronizations = synchronizations;
        _mappings = mappings;
        _sources = sources;
        _sage = sage;
        _docuWare = docuWare;
        _tracking = tracking;
        _records = records;
        _logger = logger;
    }

    public async Task<EntitySyncResult> ExecuteAsync(
        int synchronizationId,
        Guid syncId,
        bool force,
        Guid? runId,
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

        var (sourceCode, sourceType) = SynchronizationRules.SplitEndpoint(job.Source);
        if (!string.Equals(sourceType, "Sage", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Synchronization '{job.Code}' source is not a Sage configuration.");
        }

        var filters = await _synchronizations.ListFiltersAsync(synchronizationId, cancellationToken).ConfigureAwait(false);
        var (whereSql, whereParameters) = SynchronizationSourceQuery.Where(filters?.Filters ?? []);
        var inserted = new HashSet<string>(
            await _records.ListInsertedSourceIdsAsync(synchronizationId, cancellationToken).ConfigureAwait(false),
            StringComparer.Ordinal);
        await using var source = await _sources.OpenAsync(sourceCode, cancellationToken).ConfigureAwait(false);
        var keyColumns = await _sage.PrimaryKeyAsync(
            source.Connection,
            source.CommandTimeoutSeconds,
            mapping.EntityName,
            cancellationToken).ConfigureAwait(false);
        if (keyColumns.Count == 0)
        {
            keyColumns = mapping.Fields.Select(field => field.EntityFieldName).ToArray();
        }

        var selectColumns = mapping.Fields
            .Select(field => field.EntityFieldName)
            .Concat(keyColumns)
            .ToArray();
        _logger.LogInformation(
            "SyncId={SyncId} SynchronizationId={SynchronizationId} MappingId={MappingId} Source={Source} Table={Table} Key={Key} Status=Selecting",
            syncId,
            synchronizationId,
            mapping.Id,
            source.ConfigurationCode,
            mapping.EntityName,
            string.Join(",", keyColumns));
        var rows = await _sage.ReadAsync(
            source.Connection,
            source.CommandTimeoutSeconds,
            mapping.EntityName,
            selectColumns,
            whereSql,
            whereParameters,
            cancellationToken).ConfigureAwait(false);

        var created = 0;
        var updated = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = SourceKey(row, keyColumns);
            if (string.IsNullOrWhiteSpace(key))
            {
                skipped++;
                continue;
            }

            if (inserted.Contains(key))
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
                var fileName = Sanitize($"{mapping.EntityName}_{key}.json");
                var documentId = await _docuWare.CreateInCabinetAsync(mapping.CabinetName, fileName, fields, cancellationToken)
                    .ConfigureAwait(false);
                tracking.DocuWareDocumentId = documentId;
                created++;
                inserted.Add(key);

                tracking.Status = SyncStatus.Completed;
                tracking.Fingerprint = fingerprint;
                tracking.LastSuccessAt = DateTimeOffset.UtcNow;
                tracking.ErrorMessage = null;
                tracking.RetryCount = 0;
                tracking.UpdatedAt = DateTimeOffset.UtcNow;
                await _tracking.UpsertAsync(tracking, cancellationToken).ConfigureAwait(false);
                await WriteRecordAsync(
                    synchronizationId,
                    runId,
                    key,
                    fingerprint,
                    tracking.DocuWareDocumentId?.ToString(),
                    "success",
                    null,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                tracking.Status = SyncStatus.Failed;
                tracking.ErrorMessage = ex.Message;
                tracking.RetryCount++;
                tracking.UpdatedAt = DateTimeOffset.UtcNow;
                await _tracking.UpsertAsync(tracking, cancellationToken).ConfigureAwait(false);
                await WriteRecordAsync(synchronizationId, runId, key, fingerprint, null, "failed", ex.Message, cancellationToken)
                    .ConfigureAwait(false);
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

    private static string SourceKey(IReadOnlyDictionary<string, object?> row, IReadOnlyList<string> keyColumns)
    {
        var parts = keyColumns
            .Select(column => ValueConverters.ToText(row.GetValueOrDefault(column)) ?? string.Empty)
            .ToArray();
        if (parts.All(string.IsNullOrWhiteSpace))
        {
            return string.Empty;
        }

        var key = string.Join("|", parts);
        return key.Length <= 128
            ? key
            : FieldFingerprint.Compute(new Dictionary<string, object?> { ["key"] = key });
    }

    private async Task WriteRecordAsync(
        int synchronizationId,
        Guid? runId,
        string key,
        string? fingerprint,
        string? destinationId,
        string status,
        string? error,
        CancellationToken cancellationToken)
    {
        if (runId is not Guid id || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        await _records.UpsertRecordAsync(
            new SynchronizationRecordWrite
            {
                SynchronizationId = synchronizationId,
                RunId = id,
                SourceRecordId = key,
                SourceBusinessKey = key,
                SourceHash = fingerprint,
                DestinationRecordId = destinationId,
                Status = status,
                ErrorMessage = error
            },
            cancellationToken).ConfigureAwait(false);
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
