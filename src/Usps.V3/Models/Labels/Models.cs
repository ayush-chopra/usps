namespace Usps.V3.Models.Labels;

public enum LabelFormat
{
    Pdf,
    Zpl,
    Svg
}

public sealed class DomesticLabelRequest
{
    public string Service { get; init; } = string.Empty;
    public LabelFormat Format { get; init; } = LabelFormat.Pdf;
    public Address ShipFrom { get; init; } = new();
    public Address ShipTo { get; init; } = new();
    public decimal WeightOz { get; init; }
    public string? Reference { get; init; }
}

public sealed class Address
{
    public string Name { get; init; } = string.Empty;
    public string AddressLine1 { get; init; } = string.Empty;
    public string? AddressLine2 { get; init; }
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string ZipCode { get; init; } = string.Empty;
}

public sealed class DomesticLabelResponse
{
    public string TrackingNumber { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public byte[] Content { get; init; } = Array.Empty<byte>();
}

