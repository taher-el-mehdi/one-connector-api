using DocuWareSageConnector.Domain.Entities;
using DocuWareSageConnector.Domain.Enums;

namespace DocuWareSageConnector.Application.Interfaces;

public interface ISageEntityRepository
{
    EntityType EntityType { get; }

    Task TestConnectionAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SageEntityRecord>> GetAllAsync(CancellationToken cancellationToken);

    Task<SageEntityRecord?> GetByKeyAsync(string key, CancellationToken cancellationToken);

    Task UpdateAsync(
        string key,
        IReadOnlyDictionary<string, object?> writableColumns,
        int cbMarq,
        CancellationToken cancellationToken);

    Task InsertAsync(
        IReadOnlyDictionary<string, object?> columns,
        CancellationToken cancellationToken);
}

public interface ISageRepositoryFactory
{
    ISageEntityRepository Get(EntityType entityType);

    Task TestConnectionAsync(CancellationToken cancellationToken);
}
