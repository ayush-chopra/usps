using System;

namespace Usps.V3.Options;

public enum UspsEnvironment
{
    Tem,
    Prod
}

public sealed class UspsPathsOptions
{
    public string AddressesStandardize { get; set; } = "addresses/standardize";
    public string PricesDomestic { get; set; } = "prices/domestic";
    public string PricesInternational { get; set; } = "prices/international";
    public string ServiceStandardsLookup { get; set; } = "servicestandards/lookup";
    public string ServiceStandardsFiles { get; set; } = "servicestandards/files";
    public string LabelsDomestic { get; set; } = "labels/domestic";
    public string ShippingOptionsQuote { get; set; } = "shippingoptions/quote";
}

public sealed class UspsOptions
{
    public UspsEnvironment Environment { get; set; } = UspsEnvironment.Tem;
    public string BaseUrl { get; set; } = "https://apis-tem.usps.com/v3/";
    public string OAuthTokenUrl { get; set; } = "https://apis-tem.usps.com/oauth2/v3/token";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    public bool PaymentsEnabled { get; set; }
    public string? PaymentsTokenUrl { get; set; }

    public UspsPathsOptions Paths { get; } = new();

    public void ApplyEnvironmentDefaults()
    {
        if (Environment == UspsEnvironment.Prod)
        {
            BaseUrl = "https://apis.usps.com/v3/";
            OAuthTokenUrl = "https://apis.usps.com/oauth2/v3/token";
        }
    }
}

