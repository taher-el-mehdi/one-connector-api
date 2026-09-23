using DocuWareSageConnector.Application.DTOs;

namespace DocuWareSageConnector.Application.Interfaces;

public interface IEntityMappingStore
{
    Task<EntityMappingListDto> ListAsync(CancellationToken cancellationToken);

    Task<EntityMappingDto?> GetAsync(int id, CancellationToken cancellationToken);

    Task<EntityMappingDto> CreateAsync(SaveEntityMappingRequest request, Guid userId, CancellationToken cancellationToken);

    Task<EntityMappingDto?> UpdateAsync(int id, SaveEntityMappingRequest request, Guid userId, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);

    Task<EntityMappingFieldDto?> AddFieldAsync(
        int mappingId,
        SaveEntityMappingFieldRequest request,
        Guid userId,
        CancellationToken cancellationToken);

    Task<bool> DeleteFieldAsync(int mappingId, int fieldId, CancellationToken cancellationToken);

    Task<MappingLabels> GetLabelsAsync(CancellationToken cancellationToken);
}

public sealed class MappingLabels
{
    public static MappingLabels Empty { get; } = new(
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public MappingLabels(
        IReadOnlyDictionary<string, string> cabinetByEntity,
        IReadOnlyDictionary<string, string> entityByCabinet)
    {
        CabinetByEntity = cabinetByEntity;
        EntityByCabinet = entityByCabinet;
    }

    public IReadOnlyDictionary<string, string> CabinetByEntity { get; }

    public IReadOnlyDictionary<string, string> EntityByCabinet { get; }

    public static MappingLabels From(IReadOnlyList<EntityMappingDto> mappings)
    {
        var cabinets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var entities = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings)
        {
            Add(cabinets, mapping.EntityName, mapping.CabinetName);
            Add(entities, mapping.CabinetName, mapping.EntityName);
        }

        return new MappingLabels(Join(cabinets), Join(entities));
    }

    public string? CabinetFor(string entityName) => Find(CabinetByEntity, entityName);

    public string? EntityFor(string cabinetName) => Find(EntityByCabinet, cabinetName);

    private static void Add(Dictionary<string, List<string>> target, string key, string value)
    {
        if (!target.TryGetValue(key, out var values))
        {
            values = [];
            target[key] = values;
        }

        if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(value);
        }
    }

    private static Dictionary<string, string> Join(Dictionary<string, List<string>> source) =>
        source.ToDictionary(pair => pair.Key, pair => string.Join(", ", pair.Value), StringComparer.OrdinalIgnoreCase);

    private static string? Find(IReadOnlyDictionary<string, string> source, string key) =>
        source.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}

public sealed class MappingConflictException : Exception
{
    public MappingConflictException(string message)
        : base(message)
    {
    }
}

public sealed class MappingStoreException : Exception
{
    public MappingStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
