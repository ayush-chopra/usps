using System.Net.Mime;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

var portStr = Environment.GetEnvironmentVariable("MOCK_PORT") ?? "9091";
var port = int.TryParse(portStr, out var p) ? p : 9091;
var server = WireMockServer.Start(port);

Console.WriteLine($"USPS Mock Server running on http://localhost:{port}");
Console.WriteLine("Routes:");
Console.WriteLine("- POST /addresses/standardize");
Console.WriteLine("- POST /prices/domestic");
Console.WriteLine("- POST /prices/international");
Console.WriteLine("- POST /servicestandards/lookup");
Console.WriteLine("- GET  /servicestandards/files");
Console.WriteLine("- POST /labels/domestic (Accept: application/pdf|text/plain|image/svg+xml)");
Console.WriteLine("- POST /shippingoptions/quote");

string Fx(string path) => Path.Combine(AppContext.BaseDirectory, "Fixtures", path);

byte[] ReadBytes(string path)
{
    var full = Fx(path);
    if (File.Exists(full)) return File.ReadAllBytes(full);
    if (File.Exists(full + ".b64"))
    {
        var b64 = File.ReadAllText(full + ".b64");
        return Convert.FromBase64String(b64);
    }
    throw new FileNotFoundException(full);
}
string ReadText(string path) => File.ReadAllText(Fx(path));

// Addresses
server.Given(Request.Create().WithPath("/addresses/standardize").UsingPost())
    .RespondWith(Response.Create()
        .WithStatusCode(200)
        .WithHeader("Content-Type", MediaTypeNames.Application.Json)
        .WithBody(ReadText(Path.Combine("addresses", "standardize.json"))));

// Domestic prices
server.Given(Request.Create().WithPath("/prices/domestic").UsingPost())
    .RespondWith(Response.Create()
        .WithStatusCode(200)
        .WithHeader("Content-Type", MediaTypeNames.Application.Json)
        .WithBody(ReadText(Path.Combine("prices-domestic", "quote.json"))));

// International prices
server.Given(Request.Create().WithPath("/prices/international").UsingPost())
    .RespondWith(Response.Create()
        .WithStatusCode(200)
        .WithHeader("Content-Type", MediaTypeNames.Application.Json)
        .WithBody(ReadText(Path.Combine("prices-international", "quote.json"))));

// Service standards
server.Given(Request.Create().WithPath("/servicestandards/lookup").UsingPost())
    .RespondWith(Response.Create()
        .WithStatusCode(200)
        .WithHeader("Content-Type", MediaTypeNames.Application.Json)
        .WithBody(ReadText(Path.Combine("servicestandards", "lookup.json"))));

server.Given(Request.Create().WithPath("/servicestandards/files").UsingGet())
    .RespondWith(Response.Create()
        .WithStatusCode(200)
        .WithHeader("Content-Type", MediaTypeNames.Application.Json)
        .WithBody(ReadText(Path.Combine("servicestandards", "files.json"))));

// Labels: content negotiation
server.Given(Request.Create().WithPath("/labels/domestic").UsingPost().WithHeader("Accept", MediaTypeNames.Application.Pdf))
    .RespondWith(Response.Create()
        .WithStatusCode(200)
        .WithHeader("Content-Type", MediaTypeNames.Application.Pdf)
        .WithHeader("X-Tracking-Number", "9400111899223857268499")
        .WithBody(ReadBytes(Path.Combine("labels", "sample.pdf"))));

server.Given(Request.Create().WithPath("/labels/domestic").UsingPost().WithHeader("Accept", "image/svg+xml"))
    .RespondWith(Response.Create()
        .WithStatusCode(200)
        .WithHeader("Content-Type", "image/svg+xml")
        .WithHeader("X-Tracking-Number", "9400111899223857268499")
        .WithBody(ReadText(Path.Combine("labels", "sample.svg"))));

server.Given(Request.Create().WithPath("/labels/domestic").UsingPost().WithHeader("Accept", MediaTypeNames.Text.Plain))
    .RespondWith(Response.Create()
        .WithStatusCode(200)
        .WithHeader("Content-Type", MediaTypeNames.Text.Plain)
        .WithHeader("X-Tracking-Number", "9400111899223857268499")
        .WithBody(ReadText(Path.Combine("labels", "sample.zpl"))));

// Shipping options
server.Given(Request.Create().WithPath("/shippingoptions/quote").UsingPost())
    .RespondWith(Response.Create()
        .WithStatusCode(200)
        .WithHeader("Content-Type", MediaTypeNames.Application.Json)
        .WithBody(ReadText(Path.Combine("shippingoptions", "quote.json"))));

// Error examples
server.Given(Request.Create().WithHeader("X-Mock-Error", "429").UsingAnyMethod())
    .RespondWith(Response.Create().WithStatusCode(429).WithHeader("Retry-After", "1").WithBody(ReadText(Path.Combine("common", "429.json"))));

server.Given(Request.Create().WithHeader("X-Mock-Error", "400").UsingAnyMethod())
    .RespondWith(Response.Create().WithStatusCode(400).WithBody(ReadText(Path.Combine("common", "400.json"))));

Console.WriteLine("Press Ctrl+C to stop...");
Thread.Sleep(Timeout.Infinite);
