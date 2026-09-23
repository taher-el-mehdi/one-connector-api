using System.Security.Claims;
using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocuWareSageConnector.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IConnectorIdentity _identity;

    public AuthController(IConnectorIdentity identity)
    {
        _identity = identity;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var session = await _identity.LoginAsync(request.Username, request.Password, cancellationToken).ConfigureAwait(false);
        return session is null ? Unauthorized() : Ok(session);
    }

    [HttpGet("me")]
    public async Task<ActionResult<OperatorProfile>> Me(CancellationToken cancellationToken)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(id, out var userId))
        {
            return Unauthorized();
        }

        var profile = await _identity.GetProfileAsync(userId, cancellationToken).ConfigureAwait(false);
        return profile is null ? Unauthorized() : Ok(profile);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var header = Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await _identity.LogoutAsync(header["Bearer ".Length..].Trim(), cancellationToken).ConfigureAwait(false);
        }

        return NoContent();
    }
}
