using Usps.V3.Models.Prices;

namespace Usps.V3.Clients;

public interface IInternationalPricesClient
{
    Task<InternationalPriceResponse> GetPricesAsync(InternationalPriceRequest req, CancellationToken ct = default);
}

