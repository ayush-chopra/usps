using Usps.V3.Models.ShippingOptions;

namespace Usps.V3.Clients;

public interface IShippingOptionsClient
{
    Task<ShippingOptionsQuoteResponse> QuoteAsync(ShippingOptionsQuoteRequest req, CancellationToken ct = default);
}

