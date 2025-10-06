using Usps.V3.Models.Addresses;

namespace Usps.V3.Clients;

public interface IAddressesClient
{
    Task<StandardizeAddressResponse> StandardizeAsync(StandardizeAddressRequest req, CancellationToken ct = default);
}

