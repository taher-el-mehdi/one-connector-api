using DocuWareSageConnector.Application.DTOs;

namespace DocuWareSageConnector.Application.Interfaces;

public interface IConfigurationCatalog
{
    Task<IReadOnlyList<ConfigurationEntry>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ConfigurationSettingRow>> ListRowsAsync(CancellationToken cancellationToken);

    Task<ConfigurationEntry> CreateAsync(CreateConfigurationRequest request, Guid userId, CancellationToken cancellationToken);

    Task<ImportConfigurationResult> ImportAsync(
        IReadOnlyList<ConfigurationSettingRow> rows,
        Guid userId,
        CancellationToken cancellationToken);
}
