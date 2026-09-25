using DocuWareSageConnector.Application.DTOs;

namespace DocuWareSageConnector.Application.Interfaces;

public interface IDocuWareSettingsStore
{
    Task UpdateAsync(UpdateDocuWareConfigurationRequest request, Guid userId, CancellationToken cancellationToken);

    Task SetStatusAsync(bool status, Guid userId, CancellationToken cancellationToken);
}
