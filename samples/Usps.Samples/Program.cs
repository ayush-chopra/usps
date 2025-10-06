using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Usps.V3;
using Usps.V3.Clients;
using Usps.V3.Models.Addresses;
using Usps.V3.Models.Labels;
using Usps.V3.Models.Prices;
using Usps.V3.Models.ShippingOptions;
using Usps.V3.Options;

var services = new ServiceCollection();

services.AddLogging(b => b.AddConsole());

services.AddUspsV3(opts =>
{
    var env = Environment.GetEnvironmentVariable("USPS_ENV");
    opts.Environment = string.Equals(env, "Prod", StringComparison.OrdinalIgnoreCase) ? UspsEnvironment.Prod : UspsEnvironment.Tem;
    opts.ClientId = Environment.GetEnvironmentVariable("USPS_CLIENT_ID") ?? string.Empty;
    opts.ClientSecret = Environment.GetEnvironmentVariable("USPS_CLIENT_SECRET") ?? string.Empty;
});

var sp = services.BuildServiceProvider();

var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Sample");
var addresses = sp.GetRequiredService<IAddressesClient>();
var shipping = sp.GetRequiredService<IShippingOptionsClient>();
var labels = sp.GetRequiredService<IDomesticLabelsClient>();

logger.LogInformation("Starting USPS V3 sample");

// 1) Standardize an address
var std = await addresses.StandardizeAsync(new StandardizeAddressRequest
{
    Addresses = new()
    {
        new AddressInput { AddressLine1 = "475 L'Enfant Plaza SW", City = "Washington", State = "DC", ZipCode = "20260" }
    }
});

logger.LogInformation("Standardized: {Count} addresses", std.Addresses.Count);

// 2) Shipping Options 3.0
var quote = await shipping.QuoteAsync(new ShippingOptionsQuoteRequest
{
    OriginZip = "10001",
    DestinationZip = "94105",
    WeightOz = 8
});

foreach (var opt in quote.Options)
{
    logger.LogInformation("Option {Service}: ${Price} ETA {Days}d", opt.Service, opt.Price, opt.EstimatedDays);
}

// 3) Label (optional)
var labelsEnabled = (Environment.GetEnvironmentVariable("USPS_LABELS_ENABLED") ?? "false").Equals("true", StringComparison.OrdinalIgnoreCase);
if (labelsEnabled)
{
    var res = await labels.CreateAsync(new DomesticLabelRequest
    {
        Service = "Ground Advantage",
        Format = LabelFormat.Pdf,
        WeightOz = 8,
        ShipFrom = new Usps.V3.Models.Labels.Address { Name = "Sender", AddressLine1 = "123 Main St", City = "New York", State = "NY", ZipCode = "10001" },
        ShipTo = new Usps.V3.Models.Labels.Address { Name = "Receiver", AddressLine1 = "1 Market St", City = "San Francisco", State = "CA", ZipCode = "94105" }
    });

    var outDir = Path.Combine(Directory.GetCurrentDirectory(), "out");
    Directory.CreateDirectory(outDir);
    var outFile = Path.Combine(outDir, "label.pdf");
    await File.WriteAllBytesAsync(outFile, res.Content);
    logger.LogInformation("Saved label to {File}", outFile);
}

logger.LogInformation("Sample complete");

