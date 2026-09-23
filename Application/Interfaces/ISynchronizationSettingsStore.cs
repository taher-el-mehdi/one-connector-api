using DocuWareSageConnector.Application.DTOs;

namespace DocuWareSageConnector.Application.Interfaces;

public interface ISynchronizationSettingsStore
{
    Task<SynchronizationFieldRequirements> ReadRequirementsAsync(CancellationToken cancellationToken);

    Task UpdateAsync(UpdateSynchronizationConfigurationRequest request, Guid userId, CancellationToken cancellationToken);
}
