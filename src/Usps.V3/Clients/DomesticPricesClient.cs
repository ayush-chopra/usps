using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Usps.V3.Models.Prices;
using Usps.V3.Options;

namespace Usps.V3.Clients;

internal sealed class DomesticPricesClient : IDomesticPricesClient
{
    private readonly HttpClient _http;
    private readonly UspsOptions _opts;
    private readonly ILogger<DomesticPricesClient> _logger;

    public DomesticPricesClient(HttpClient http, IOptions<Usps.V3.Options.UspsOptions> options, ILogger<DomesticPricesClient> logger)
    {
        _http = http;
        _opts = options.Value;
        _logger = logger;
    }

    public Task<DomesticPriceResponse> GetPricesAsync(DomesticPriceRequest req, CancellationToken ct = default)
    {
        _logger.LogInformation("Domestic prices for {Origin}->{Dest}", req.OriginZip, req.DestinationZip);
        return _http.PostJsonAsync<DomesticPriceResponse>(_opts.Paths.PricesDomestic, req, ct);
    }
}

