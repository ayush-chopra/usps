using Usps.V3.Models.Labels;

namespace Usps.V3.Clients;

public interface IDomesticLabelsClient
{
    Task<DomesticLabelResponse> CreateAsync(DomesticLabelRequest req, CancellationToken ct = default);
}

