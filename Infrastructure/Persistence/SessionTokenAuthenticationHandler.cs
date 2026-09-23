using System.Security.Claims;
using System.Text.Encodings.Web;
using DocuWareSageConnector.Application.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DocuWareSageConnector.Infrastructure.Persistence;

public static class ConnectorSessionDefaults
{
    public const string Scheme = "ConnectorSession";
}

public sealed class SessionTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IConnectorIdentity _identity;

    public SessionTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConnectorIdentity identity)
        : base(options, logger, encoder)
    {
        _identity = identity;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = header["Bearer ".Length..].Trim();
        if (token.Length is < 1 or > 512)
        {
            return AuthenticateResult.Fail("The session is not valid.");
        }

        var op = await _identity.AuthenticateAsync(token, Context.RequestAborted).ConfigureAwait(false);
        if (op is null)
        {
            return AuthenticateResult.Fail("The session is not valid.");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, op.UserId.ToString("D")),
            new Claim(ClaimTypes.Name, op.Username)
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
