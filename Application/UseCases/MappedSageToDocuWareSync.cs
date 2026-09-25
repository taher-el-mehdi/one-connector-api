using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.Application.Mapping;
using DocuWareSageConnector.Application.Synchronization;
using DocuWareSageConnector.Domain.Entities;
using DocuWareSageConnector.Domain.Enums;
using DocuWareSageConnector.Domain.Mapping;
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
    private readonly ISynchronizationExecutionStore _records;
    private readonly ILogger<MappedSageToDocuWareSync> _logger;

    public MappedSageToDocuWareSync(
        ISynchronizationStore synchronizations,
        IEntityMappingStore mappings,
        SageSourceConnection sources,
        SageMappedTableReader sage,
        IDocuWareDocumentService docuWare,
        ISynchronizationExecutionStore records,
        ILogger<MappedSageToDocuWareSync> logger)
    {
        _synchronizations = synchronizations;
        _mappings = mappings;
        _sources = sources;
        _sage = sage;
        _docuWare = docuWare;
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
            await _records.ListInsertedEntityIdsAsync(synchronizationId, cancellationToken).ConfigureAwait(false),
            StringComparer.Ordinal);
        await using var source = await _sources.OpenAsync(sourceCode, cancellationToken).ConfigureAwait(false);
        var keyColumns = EntityMappingRules.CompositeKey(mapping.Fields);
        if (keyColumns.Count == 0)
        {
            keyColumns = await _sage.PrimaryKeyAsync(
                source.Connection,
                source.CommandTimeoutSeconds,
                mapping.EntityName,
                cancellationToken).ConfigureAwait(false);
        }

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
            var entityId = EntityId(row, keyColumns);
            if (string.IsNullOrWhiteSpace(entityId))
            {
                skipped++;
                continue;
            }

            if (inserted.Contains(entityId))
            {
                skipped++;
                continue;
            }

            var fields = MapFields(mapping.Fields, row);

            try
            {
                var fileName = Sanitize($"{mapping.EntityName}_{entityId}.json");
                var documentId = await _docuWare.CreateInCabinetAsync(mapping.CabinetName, fileName, fields, cancellationToken)
                    .ConfigureAwait(false);
                created++;
                inserted.Add(entityId);
                await WriteRecordAsync(
                    synchronizationId,
                    runId,
                    entityId,
                    documentId.ToString(),
                    null,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                await WriteRecordAsync(synchronizationId, runId, entityId, null, ex.Message, cancellationToken)
                    .ConfigureAwait(false);
                _logger.LogError(ex, "SyncId={SyncId} SynchronizationId={SynchronizationId} EntityId={EntityId} Status=Failed", syncId, synchronizationId, entityId);
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

    private static string EntityId(IReadOnlyDictionary<string, object?> row, IReadOnlyList<string> keyColumns)
    {
        var parts = keyColumns
            .Select(column => ValueConverters.ToText(row.GetValueOrDefault(column)) ?? string.Empty)
            .ToArray();
        if (parts.All(string.IsNullOrWhiteSpace))
        {
            return string.Empty;
        }

        return string.Join("|", parts);
    }

    private async Task WriteRecordAsync(
        int synchronizationId,
        Guid? runId,
        string entityId,
        string? docuWareId,
        string? error,
        CancellationToken cancellationToken)
    {
        if (runId is not Guid id || string.IsNullOrWhiteSpace(entityId))
        {
            return;
        }

        await _records.UpsertRecordAsync(
            new SynchronizationRecordWrite
            {
                SynchronizationId = synchronizationId,
                RunId = id,
                EntityId = entityId,
                DocuWareId = docuWareId,
                Error = error
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
