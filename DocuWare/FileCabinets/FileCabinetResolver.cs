using DocuWare.Platform.ServerClient;
using DocuWareSageConnector.Domain.Enums;
using DocuWareSageConnector.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DocuWareSageConnector.DocuWare.FileCabinets;

public sealed class FileCabinetResolver
{
    private readonly DocuWareOptions _options;

    public FileCabinetResolver(IOptions<DocuWareOptions> options)
    {
        _options = options.Value;
    }

    public Organization ResolveOrganization(ServiceConnection connection)
    {
        var organizations = connection.Organizations?.ToList()
            ?? throw new InvalidOperationException("DocuWare returned no organizations.");

        if (organizations.Count == 0)
        {
            throw new InvalidOperationException("DocuWare returned an empty organization list.");
        }

        if (string.IsNullOrWhiteSpace(_options.Organization))
        {
            return organizations[0];
        }

        var match = organizations.FirstOrDefault(org =>
            string.Equals(org.Name, _options.Organization, StringComparison.OrdinalIgnoreCase));

        return match
               ?? throw new InvalidOperationException(
                   $"Organization '{_options.Organization}' was not found. Available: {string.Join(", ", organizations.Select(org => org.Name))}");
    }

    public FileCabinet ResolveCabinet(IReadOnlyList<FileCabinet> cabinets, EntityType entityType)
    {
        var configured = _options.FileCabinets.For(entityType);
        var id = configured.Id?.Trim();
        var name = configured.Name?.Trim();

        FileCabinet? match = null;
        if (!string.IsNullOrWhiteSpace(id))
        {
            match = cabinets.FirstOrDefault(cabinet =>
                string.Equals(cabinet.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        if (match is null && !string.IsNullOrWhiteSpace(name))
        {
            match = cabinets.FirstOrDefault(cabinet =>
                string.Equals(cabinet.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));
        }

        if (match is not null)
        {
            return match;
        }

        var available = string.Join(", ", cabinets.Select(cabinet => $"{cabinet.Name} ({cabinet.Id})"));
        throw new InvalidOperationException(
            $"File cabinet for {entityType} was not found (Name='{name}', Id='{id}'). Available: {available}");
    }
}
