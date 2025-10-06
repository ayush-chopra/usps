using Microsoft.Extensions.Logging;

namespace Usps.V3.Http;

internal sealed class CorrelationIdHandler : DelegatingHandler
{
    private const string HeaderName = "X-Correlation-ID";
    private readonly ILogger<CorrelationIdHandler> _logger;

    public CorrelationIdHandler(ILogger<CorrelationIdHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(HeaderName))
        {
            var id = Guid.NewGuid().ToString("N");
            request.Headers.TryAddWithoutValidation(HeaderName, id);
            _logger.LogDebug("Generated {Header}={Id}", HeaderName, id);
        }
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response;
    }
}

