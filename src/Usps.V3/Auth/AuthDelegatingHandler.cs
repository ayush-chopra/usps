using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace Usps.V3.Auth;

internal sealed class AuthDelegatingHandler : DelegatingHandler
{
    private readonly IUspsTokenClient _tokenClient;
    private readonly ILogger<AuthDelegatingHandler> _logger;

    public AuthDelegatingHandler(IUspsTokenClient tokenClient, ILogger<AuthDelegatingHandler> logger)
    {
        _tokenClient = tokenClient;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokenClient.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _logger.LogDebug("Authorization header set (Bearer ****)");
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

