namespace Usps.V3.Models.Prices;

public sealed class DomesticPriceRequest
{
    public string OriginZip { get; init; } = string.Empty;
    public string DestinationZip { get; init; } = string.Empty;
    public decimal WeightOz { get; init; }
    public Dimensions? Dimensions { get; init; }
    public string? ServiceGroup { get; init; }
}

public sealed class InternationalPriceRequest
{
    public string DestinationCountryCode { get; init; } = string.Empty;
    public decimal WeightOz { get; init; }
    public Dimensions? Dimensions { get; init; }
    public string? ContentsType { get; init; }
}

public sealed class Dimensions
{
    public decimal LengthIn { get; init; }
    public decimal WidthIn { get; init; }
    public decimal HeightIn { get; init; }
}

public sealed class PriceQuote
{
    public string Service { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string Currency { get; init; } = "USD";
    public string? DeliveryStandard { get; init; }
}

public sealed class DomesticPriceResponse
{
    public List<PriceQuote> Quotes { get; init; } = new();
}

public sealed class InternationalPriceResponse
{
    public List<PriceQuote> Quotes { get; init; } = new();
}

