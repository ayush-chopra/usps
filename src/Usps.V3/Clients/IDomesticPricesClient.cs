using Usps.V3.Models.Prices;

namespace Usps.V3.Clients;

public interface IDomesticPricesClient
{
    Task<DomesticPriceResponse> GetPricesAsync(DomesticPriceRequest req, CancellationToken ct = default);
}

