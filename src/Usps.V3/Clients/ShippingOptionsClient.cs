using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Usps.V3.Models.ShippingOptions;
using Usps.V3.Options;

namespace Usps.V3.Clients;

internal sealed class ShippingOptionsClient : IShippingOptionsClient
{
    private readonly HttpClient _http;
    private readonly UspsOptions _opts;
    private readonly ILogger<ShippingOptionsClient> _logger;

    public ShippingOptionsClient(HttpClient http, IOptions<UspsOptions> options, ILogger<ShippingOptionsClient> logger)
    {
        _http = http;
        _opts = options.Value;
        _logger = logger;
    }

    public Task<ShippingOptionsQuoteResponse> QuoteAsync(ShippingOptionsQuoteRequest req, CancellationToken ct = default)
    {
        _logger.LogInformation("Shipping options {Origin}->{Dest}", req.OriginZip, req.DestinationZip);
        return _http.PostJsonAsync<ShippingOptionsQuoteResponse>(_opts.Paths.ShippingOptionsQuote, req, ct);
    }
}

