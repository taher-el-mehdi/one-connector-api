using System.Text;
using System.Text.Json;
using DocuWare.Platform.ServerClient;
using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using DocuWareSageConnector.DocuWare.Authentication;
using DocuWareSageConnector.DocuWare.FileCabinets;
using DocuWareSageConnector.DocuWare.Mapping;
using DocuWareSageConnector.Domain.Entities;
using DocuWareSageConnector.Domain.Enums;
using DocuWareSageConnector.Domain.Mapping;
using DocuWareSageConnector.Infrastructure.Synchronization;

namespace DocuWareSageConnector.DocuWare.Clients;

public sealed class DocuWareDocumentService : IDocuWareDocumentService, IAsyncDisposable
{
    private readonly IDocuWareConnectionFactory _connectionFactory;
    private readonly FileCabinetResolver _cabinetResolver;
    private readonly IResilienceExecutor _resilience;
    private readonly ILogger<DocuWareDocumentService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ServiceConnection? _connection;
    private Organization? _organization;
    private readonly Dictionary<EntityType, FileCabinet> _cabinets = new();
    private readonly Dictionary<EntityType, Dialog> _dialogs = new();

    public DocuWareDocumentService(
        IDocuWareConnectionFactory connectionFactory,
        FileCabinetResolver cabinetResolver,
        IResilienceExecutor resilience,
        ILogger<DocuWareDocumentService> logger)
    {
        _connectionFactory = connectionFactory;
        _cabinetResolver = cabinetResolver;
        _resilience = resilience;
        _logger = logger;
    }

    public async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        foreach (var entityType in Enum.GetValues<EntityType>())
        {
            await ResolveCabinetAsync(entityType, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<string>> ListFileCabinetNamesAsync(CancellationToken cancellationToken)
    {
        var cabinets = await ListFileCabinetsAsync(cancellationToken).ConfigureAwait(false);
        return cabinets.Select(cabinet => string.IsNullOrWhiteSpace(cabinet.Name) ? cabinet.Id : cabinet.Name).ToArray();
    }

    public async Task<IReadOnlyList<DocuWareCabinet>> ListFileCabinetsAsync(CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var cabinets = await LoadCabinetsAsync(cancellationToken).ConfigureAwait(false);
        return cabinets
            .Select(cabinet => new DocuWareCabinet
            {
                Id = cabinet.Id?.Trim() ?? string.Empty,
                Name = cabinet.Name?.Trim() ?? string.Empty,
                IsBasket = cabinet.IsBasket
            })
            .Where(cabinet => cabinet.Id.Length > 0 || cabinet.Name.Length > 0)
            .OrderBy(cabinet => cabinet.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(cabinet => cabinet.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<DocuWareCabinetDetail?> GetFileCabinetAsync(string cabinetId, CancellationToken cancellationToken)
    {
        var requestedId = cabinetId.Trim();
        if (requestedId.Length == 0)
        {
            return null;
        }

        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var cabinets = await LoadCabinetsAsync(cancellationToken).ConfigureAwait(false);
        var match = cabinets.FirstOrDefault(cabinet =>
            string.Equals(cabinet.Id, requestedId, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return null;
        }

        var full = match.GetFileCabinetFromSelfRelation();
        return new DocuWareCabinetDetail
        {
            Id = full.Id?.Trim() ?? match.Id?.Trim() ?? requestedId,
            Name = full.Name?.Trim() ?? match.Name?.Trim() ?? string.Empty,
            IsBasket = full.IsBasket,
            Fields = MapFields(full.Fields)
        };
    }

    private static IReadOnlyList<DocuWareCabinetField> MapFields(IEnumerable<FileCabinetField>? fields)
    {
        return (fields ?? [])
            .Select(field => new DocuWareCabinetField
            {
                DatabaseName = field.DBFieldName?.Trim() ?? string.Empty,
                DisplayName = field.DisplayName?.Trim() ?? field.DBFieldName?.Trim() ?? string.Empty,
                Type = field.DWFieldType.ToString(),
                Scope = field.Scope.ToString(),
                Length = field.Length,
                Precision = field.Precision,
                Description = string.IsNullOrWhiteSpace(field.FieldInfoText) ? null : field.FieldInfoText.Trim(),
                Columns = field.TableFieldColumns?
                    .Select(column => column.DisplayName?.Trim() ?? column.DBFieldName?.Trim() ?? string.Empty)
                    .Where(name => name.Length > 0)
                    .ToArray() ?? []
            })
            .Where(field => field.DatabaseName.Length > 0 || field.DisplayName.Length > 0)
            .ToArray();
    }

    public async Task<string> ResolveFileCabinetIdAsync(EntityType entityType, CancellationToken cancellationToken)
    {
        var cabinet = await ResolveCabinetAsync(entityType, cancellationToken).ConfigureAwait(false);
        return cabinet.Id ?? throw new InvalidOperationException($"File cabinet for {entityType} has no Id.");
    }

    public Task<DocuWareDocumentInfo?> FindByKeyAsync(
        EntityType entityType,
        string keyValue,
        CancellationToken cancellationToken) =>
        _resilience.ExecuteAsync(
            ct => QuerySingleAsync(entityType, EntityFieldMaps.KeyOf(entityType).DocuWareField, keyValue, ct),
            cancellationToken);

    public Task<DocuWareDocumentInfo?> GetByIdAsync(
        EntityType entityType,
        int documentId,
        CancellationToken cancellationToken) =>
        _resilience.ExecuteAsync(
            ct => QuerySingleAsync(entityType, "DWDOCID", documentId.ToString(), ct),
            cancellationToken);

    public Task<IReadOnlyList<DocuWareDocumentInfo>> ListDocumentsAsync(
        EntityType entityType,
        CancellationToken cancellationToken) =>
        _resilience.ExecuteAsync(ct => QueryAllAsync(entityType, ct), cancellationToken);

    public Task UpdateIndexFieldsAsync(
        EntityType entityType,
        int documentId,
        IReadOnlyList<IndexFieldValue> fields,
        CancellationToken cancellationToken) =>
        _resilience.ExecuteAsync(
            async ct =>
            {
                var document = await LoadDocumentAsync(entityType, documentId, ct).ConfigureAwait(false);
                var update = new DocumentIndexFields
                {
                    Field = fields.Select(DocuWareIndexFieldMapper.ToSdkField).ToList()
                };

                await document.PutToFieldsRelationForDocumentIndexFieldsAsync(update).ConfigureAwait(false);
                return true;
            },
            cancellationToken);

    public Task<int> CreateDocumentAsync(
        EntityType entityType,
        string fileName,
        IReadOnlyList<IndexFieldValue> fields,
        CancellationToken cancellationToken) =>
        _resilience.ExecuteAsync(
            async ct =>
            {
                var cabinet = await ResolveCabinetAsync(entityType, ct).ConfigureAwait(false);
                var sdkFields = fields.Select(DocuWareIndexFieldMapper.ToSdkField).ToArray();
                var payload = JsonSerializer.Serialize(
                    fields.ToDictionary(field => field.Name, field => field.Value),
                    new JsonSerializerOptions { WriteIndented = true });

                var tempPath = Path.Combine(Path.GetTempPath(), fileName);
                await File.WriteAllTextAsync(tempPath, payload, Encoding.UTF8, ct).ConfigureAwait(false);
                try
                {
                    var uploaded = await cabinet
                        .EasyUploadSingleDocumentAsync(new FileInfo(tempPath), sdkFields)
                        .ConfigureAwait(false);
                    return uploaded.Content.Id;
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
            },
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _connection?.Disconnect();
            _connection = null;
            _organization = null;
            _cabinets.Clear();
            _dialogs.Clear();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task<DocuWareDocumentInfo?> QuerySingleAsync(
        EntityType entityType,
        string fieldName,
        string fieldValue,
        CancellationToken cancellationToken)
    {
        var dialog = await ResolveDialogAsync(entityType, cancellationToken).ConfigureAwait(false);
        var expression = new DialogExpression
        {
            Operation = DialogExpressionOperation.And,
            Condition =
            [
                DialogExpressionCondition.Create(fieldName, fieldValue)
            ],
            Count = 2
        };

        var result = await dialog.GetDocumentsResultAsync(expression).ConfigureAwait(false);
        var items = result.Items ?? [];
        if (items.Count > 1)
        {
            _logger.LogWarning(
                "Duplicate DocuWare documents for {EntityType} field {FieldName}={FieldValue}. Using the first document.",
                entityType,
                fieldName,
                fieldValue);
        }

        var document = items.FirstOrDefault();
        return document is null ? null : DocuWareIndexFieldMapper.ToInfo(document, entityType);
    }

    private async Task<IReadOnlyList<DocuWareDocumentInfo>> QueryAllAsync(
        EntityType entityType,
        CancellationToken cancellationToken)
    {
        var dialog = await ResolveDialogAsync(entityType, cancellationToken).ConfigureAwait(false);
        var keyField = EntityFieldMaps.KeyOf(entityType).DocuWareField;
        var expression = new DialogExpression
        {
            Operation = DialogExpressionOperation.And,
            Condition = [],
            Count = 500,
            SortOrder =
            [
                SortedField.Create(keyField, SortDirection.Asc)
            ]
        };

        var documents = new List<DocuWareDocumentInfo>();
        var result = await dialog.GetDocumentsResultAsync(expression).ConfigureAwait(false);
        while (result is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var document in result.Items ?? [])
            {
                documents.Add(DocuWareIndexFieldMapper.ToInfo(document, entityType));
            }

            if (string.IsNullOrWhiteSpace(result.NextRelationLink))
            {
                break;
            }

            result = await result.GetDocumentsQueryResultFromNextRelationAsync().ConfigureAwait(false);
        }

        return documents;
    }

    private async Task<Document> LoadDocumentAsync(
        EntityType entityType,
        int documentId,
        CancellationToken cancellationToken)
    {
        var dialog = await ResolveDialogAsync(entityType, cancellationToken).ConfigureAwait(false);
        var expression = new DialogExpression
        {
            Operation = DialogExpressionOperation.And,
            Condition =
            [
                DialogExpressionCondition.Create("DWDOCID", documentId.ToString())
            ],
            Count = 1
        };

        var result = await dialog.GetDocumentsResultAsync(expression).ConfigureAwait(false);
        return result.Items?.FirstOrDefault()
               ?? throw new InvalidOperationException($"DocuWare document {documentId} was not found.");
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is not null)
            {
                return;
            }

            _connection = await _connectionFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
            _organization = _cabinetResolver.ResolveOrganization(_connection);
        }
        catch
        {
            ResetConnection();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResetAndReconnectAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ResetConnection();
        }
        finally
        {
            _gate.Release();
        }

        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
    }

    private void ResetConnection()
    {
        try
        {
            _connection?.Disconnect();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DocuWare disconnect failed during reset.");
        }

        _connection = null;
        _organization = null;
        _cabinets.Clear();
        _dialogs.Clear();
    }

    private async Task<IReadOnlyList<FileCabinet>> LoadCabinetsAsync(CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var organization = _organization ?? throw new InvalidOperationException("DocuWare organization is not resolved.");
        var result = organization.GetFileCabinetsFromFilecabinetsRelation();
        return result.FileCabinet ?? [];
    }

    private async Task<FileCabinet> ResolveCabinetAsync(EntityType entityType, CancellationToken cancellationToken)
    {
        if (_cabinets.TryGetValue(entityType, out var cached))
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cabinets.TryGetValue(entityType, out cached))
            {
                return cached;
            }

            var cabinets = await LoadCabinetsAsync(cancellationToken).ConfigureAwait(false);
            var resolved = _cabinetResolver.ResolveCabinet(cabinets, entityType);
            _cabinets[entityType] = resolved;
            return resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dialog> ResolveDialogAsync(EntityType entityType, CancellationToken cancellationToken)
    {
        if (_dialogs.TryGetValue(entityType, out var cached))
        {
            return cached;
        }

        var cabinet = await ResolveCabinetAsync(entityType, cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_dialogs.TryGetValue(entityType, out cached))
            {
                return cached;
            }

            var dialogInfos = cabinet.GetDialogInfosFromSearchesRelation();
            var dialogInfo = dialogInfos.Dialog?.FirstOrDefault()
                             ?? throw new InvalidOperationException(
                                 $"No search dialog is available for file cabinet '{cabinet.Name}'.");
            var dialog = dialogInfo.GetDialogFromSelfRelation();
            _dialogs[entityType] = dialog;
            return dialog;
        }
        finally
        {
            _gate.Release();
        }
    }
}
