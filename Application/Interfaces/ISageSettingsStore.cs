using DocuWareSageConnector.Application.DTOs;

namespace DocuWareSageConnector.Application.Interfaces;

public interface ISageSettingsStore
{
    Task<SageFieldRequirements> ReadRequirementsAsync(CancellationToken cancellationToken);

    Task UpdateAsync(UpdateSageConfigurationRequest request, Guid userId, CancellationToken cancellationToken);

    Task SetStatusAsync(bool status, Guid userId, CancellationToken cancellationToken);
}
