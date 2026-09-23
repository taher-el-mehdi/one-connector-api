using DocuWareSageConnector.Application.DTOs;

namespace DocuWareSageConnector.Application.Interfaces;

public interface ILeadStore
{
    Task<Guid> AddAsync(NewLead lead, CancellationToken cancellationToken);
}
