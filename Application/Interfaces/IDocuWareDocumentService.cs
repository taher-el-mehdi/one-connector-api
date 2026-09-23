using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Domain.Entities;
using DocuWareSageConnector.Domain.Enums;

namespace DocuWareSageConnector.Application.Interfaces;

public interface IDocuWareDocumentService
{
    Task<IReadOnlyList<string>> ListFileCabinetNamesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<DocuWareCabinet>> ListFileCabinetsAsync(CancellationToken cancellationToken);

    Task<DocuWareCabinetDetail?> GetFileCabinetAsync(string cabinetId, CancellationToken cancellationToken);

    Task<string> ResolveFileCabinetIdAsync(EntityType entityType, CancellationToken cancellationToken);

    Task<DocuWareDocumentInfo?> FindByKeyAsync(
        EntityType entityType,
        string keyValue,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DocuWareDocumentInfo>> ListDocumentsAsync(
        EntityType entityType,
        CancellationToken cancellationToken);

    Task<DocuWareDocumentInfo?> GetByIdAsync(
        EntityType entityType,
        int documentId,
        CancellationToken cancellationToken);

    Task UpdateIndexFieldsAsync(
        EntityType entityType,
        int documentId,
        IReadOnlyList<IndexFieldValue> fields,
        CancellationToken cancellationToken);

    Task<int> CreateDocumentAsync(
        EntityType entityType,
        string fileName,
        IReadOnlyList<IndexFieldValue> fields,
        CancellationToken cancellationToken);

    Task TestConnectionAsync(CancellationToken cancellationToken);
}
