namespace Usps.V3.Models.ServiceStandards;

public sealed class ServiceStandardsLookupRequest
{
    public string OriginZip { get; init; } = string.Empty;
    public string DestinationZip { get; init; } = string.Empty;
    public string Service { get; init; } = string.Empty;
}

public sealed class ServiceStandardsLookupResponse
{
    public string Service { get; init; } = string.Empty;
    public int EstimatedDays { get; init; }
    public DateTime? EstimatedDeliveryDate { get; init; }
}

public sealed class ServiceStandardsFilesResponse
{
    public List<string> Files { get; init; } = new();
}

