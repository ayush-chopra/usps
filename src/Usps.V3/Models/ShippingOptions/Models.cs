using Usps.V3.Models.Prices;

namespace Usps.V3.Models.ShippingOptions;

public sealed class ShippingOptionsQuoteRequest
{
    public string OriginZip { get; init; } = string.Empty;
    public string DestinationZip { get; init; } = string.Empty;
    public decimal WeightOz { get; init; }
    public Dimensions? Dimensions { get; init; }
}

public sealed class ShippingOption
{
    public string Service { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string Currency { get; init; } = "USD";
    public int EstimatedDays { get; init; }
}

public sealed class ShippingOptionsQuoteResponse
{
    public List<ShippingOption> Options { get; init; } = new();
}

