using Microsoft.Extensions.Logging;

namespace Usps.V3.Auth;

internal sealed class FakeTokenClient : IUspsTokenClient
{
    private readonly ILogger<FakeTokenClient> _logger;
    public FakeTokenClient(ILogger<FakeTokenClient> logger) => _logger = logger;

    public Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Using FakeTokenClient (mock mode)");
        return Task.FromResult("fake-token");
    }
}

