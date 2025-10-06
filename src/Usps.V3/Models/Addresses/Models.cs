namespace Usps.V3.Models.Addresses;

public sealed class StandardizeAddressRequest
{
    public List<AddressInput> Addresses { get; init; } = new();
}

public sealed class AddressInput
{
    public string AddressLine1 { get; init; } = string.Empty;
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? ZipCode { get; init; }
}

public sealed class StandardizeAddressResponse
{
    public List<AddressOutput> Addresses { get; init; } = new();
}

public sealed class AddressOutput
{
    public string AddressLine1 { get; init; } = string.Empty;
    public string? AddressLine2 { get; init; }
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string ZipCode { get; init; } = string.Empty;
    public bool Valid { get; init; }
}

