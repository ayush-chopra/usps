using Usps.V3.Models.ServiceStandards;

namespace Usps.V3.Clients;

public interface IServiceStandardsClient
{
    Task<ServiceStandardsLookupResponse> LookupAsync(ServiceStandardsLookupRequest req, CancellationToken ct = default);
    Task<ServiceStandardsFilesResponse> GetFilesAsync(CancellationToken ct = default);
}

