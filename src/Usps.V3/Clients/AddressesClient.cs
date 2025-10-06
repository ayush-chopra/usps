using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Usps.V3.Models.Addresses;
using Usps.V3.Options;

namespace Usps.V3.Clients;

internal sealed class AddressesClient : IAddressesClient
{
    private readonly HttpClient _http;
    private readonly UspsOptions _opts;
    private readonly ILogger<AddressesClient> _logger;

    public AddressesClient(HttpClient http, IOptions<Usps.V3.Options.UspsOptions> options, ILogger<AddressesClient> logger)
    {
        _http = http;
        _opts = options.Value;
        _logger = logger;
    }

    public Task<StandardizeAddressResponse> StandardizeAsync(StandardizeAddressRequest req, CancellationToken ct = default)
    {
        _logger.LogInformation("Standardizing {Count} address(es)", req.Addresses.Count);
        return _http.PostJsonAsync<StandardizeAddressResponse>(_opts.Paths.AddressesStandardize, req, ct);
    }
}

