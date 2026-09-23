using DocuWareSageConnector.Application.DTOs;

namespace DocuWareSageConnector.Application.Interfaces;

public interface IConnectorIdentity
{
    Task<LoginResponse?> LoginAsync(string username, string password, CancellationToken cancellationToken);

    Task<AuthenticatedOperator?> AuthenticateAsync(string token, CancellationToken cancellationToken);

    Task<OperatorProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> VerifyPasswordAsync(Guid userId, string username, string password, CancellationToken cancellationToken);

    Task LogoutAsync(string token, CancellationToken cancellationToken);
}
