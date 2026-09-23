using System.Net;
using System.Text.Json.Serialization;
using DocuWare.Platform.ServerClient;
using DocuWareSageConnector.Domain.Enums;
using DocuWareSageConnector.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DocuWareSageConnector.DocuWare.Authentication;

public interface IDocuWareConnectionFactory
{
    Task<ServiceConnection> CreateAsync(CancellationToken cancellationToken);
}

public sealed class DocuWareConnectionFactory : IDocuWareConnectionFactory
{
    private readonly DocuWareOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DocuWareConnectionFactory> _logger;

    public DocuWareConnectionFactory(
        IOptions<DocuWareOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<DocuWareConnectionFactory> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ServiceConnection> CreateAsync(CancellationToken cancellationToken)
    {
        var uri = new Uri(_options.PlatformUrl.TrimEnd('/') + "/");
        var organization = string.IsNullOrWhiteSpace(_options.Organization) ? null : _options.Organization.Trim();

        _logger.LogInformation(
            "Connecting to DocuWare platform. AuthenticationMode={AuthenticationMode} OrganizationConfigured={OrganizationConfigured}",
            _options.AuthenticationMode,
            !string.IsNullOrWhiteSpace(organization));

        if (_options.AuthenticationMode == DocuWareAuthenticationMode.AppRegistration)
        {
            var token = await RequestAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            return await ServiceConnection.CreateWithJwtAsync(
                    uri,
                    token,
                    DWProductTypes.PlatformService)
                .ConfigureAwait(false);
        }

        var loginData = new ServiceConnectionLoginData
        {
            Organization = organization,
            Transport = new ServiceConnectionTransportData
            {
                CancellationToken = cancellationToken
            }
        };

        return await ServiceConnection.CreateAsync(
                uri,
                _options.UserName,
                _options.Password,
                loginData)
            .ConfigureAwait(false);
    }

    private async Task<string> RequestAccessTokenAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("DocuWareIdentity");
        var platform = _options.PlatformUrl.TrimEnd('/');

        using var identityResponse = await client
            .GetAsync($"{platform}/Home/IdentityServiceInfo", cancellationToken)
            .ConfigureAwait(false);
        identityResponse.EnsureSuccessStatusCode();

        var identity = await identityResponse.Content
            .ReadFromJsonAsync<IdentityServiceInfo>(cancellationToken: cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("DocuWare IdentityServiceInfo response was empty.");

        if (string.IsNullOrWhiteSpace(identity.IdentityServiceUrl))
        {
            throw new InvalidOperationException("DocuWare IdentityServiceUrl is missing.");
        }

        using var discoveryResponse = await client
            .GetAsync($"{identity.IdentityServiceUrl.TrimEnd('/')}/.well-known/openid-configuration", cancellationToken)
            .ConfigureAwait(false);
        discoveryResponse.EnsureSuccessStatusCode();

        var discovery = await discoveryResponse.Content
            .ReadFromJsonAsync<OpenIdConfiguration>(cancellationToken: cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("OpenID configuration response was empty.");

        if (string.IsNullOrWhiteSpace(discovery.TokenEndpoint))
        {
            throw new InvalidOperationException("OpenID token_endpoint is missing.");
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = _options.UserName,
            ["password"] = _options.Password,
            ["client_id"] = _options.ClientId,
            ["scope"] = _options.Scope
        };

        if (!string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            form["client_secret"] = _options.ClientSecret;
        }

        using var tokenResponse = await client
            .PostAsync(discovery.TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken)
            .ConfigureAwait(false);

        if (tokenResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("DocuWare App Registration authentication failed.");
        }

        tokenResponse.EnsureSuccessStatusCode();
        var token = await tokenResponse.Content
            .ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(token?.AccessToken))
        {
            throw new InvalidOperationException("DocuWare token response did not contain an access token.");
        }

        return token.AccessToken;
    }

    private sealed class IdentityServiceInfo
    {
        public string? IdentityServiceUrl { get; set; }
    }

    private sealed class OpenIdConfiguration
    {
        [JsonPropertyName("token_endpoint")]
        public string? TokenEndpoint { get; set; }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }
}
