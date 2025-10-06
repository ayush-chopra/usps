using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Usps.V3.Models.Prices;
using Usps.V3.Options;

namespace Usps.V3.Clients;

internal sealed class InternationalPricesClient : IInternationalPricesClient
{
    private readonly HttpClient _http;
    private readonly UspsOptions _opts;
    private readonly ILogger<InternationalPricesClient> _logger;

    public InternationalPricesClient(HttpClient http, IOptions<Usps.V3.Options.UspsOptions> options, ILogger<InternationalPricesClient> logger)
    {
        _http = http;
        _opts = options.Value;
        _logger = logger;
    }

    public Task<InternationalPriceResponse> GetPricesAsync(InternationalPriceRequest req, CancellationToken ct = default)
    {
        _logger.LogInformation("International prices to {Country}", req.DestinationCountryCode);
        return _http.PostJsonAsync<InternationalPriceResponse>(_opts.Paths.PricesInternational, req, ct);
    }
}

