using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Usps.V3;
using Usps.V3.Clients;
using Usps.V3.Models.Addresses;
using Usps.V3.Models.ShippingOptions;
using Usps.V3.Models.Labels;
using Usps.V3.Options;
using System.Net.Mime;
using System;
using System.Threading.Tasks;
using Xunit;

public class ContractTests : IDisposable
{
    private readonly WireMockServer _server;

    public ContractTests()
    {
        _server = WireMockServer.Start();

        // Addresses standardize
        _server.Given(Request.Create().WithPath("/addresses/standardize").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"addresses\":[{\"addressLine1\":\"475 L'Enfant Plaza SW\",\"city\":\"Washington\",\"state\":\"DC\",\"zipCode\":\"20260\",\"valid\":true}]}"));

        // Shipping options quote
        _server.Given(Request.Create().WithPath("/shippingoptions/quote").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"options\":[{\"service\":\"Ground Advantage\",\"price\":7.85,\"currency\":\"USD\",\"estimatedDays\":3}]}"));

        // Label: PDF
        var pdfBytes = Convert.FromBase64String("JVBERi0xLjQKJcTl8uXrp/Og0MTGCjEgMCBvYmoKPDwvVHlwZS9DYXRhbG9nL1BhZ2VzIDIgMCBSPj4KZW5kb2JqCjIgMCBvYmoKPDwvVHlwZS9QYWdlcy9Db3VudCAxL0tpZHNbMyAwIFJdPj4KZW5kb2JqCjMgMCBvYmoKPDwvVHlwZS9QYWdlL1BhcmVudCAyIDAgUi9NZWRpYUJveFswIDAgNTk1IDg0Ml0vQ29udGVudHMgNCAwIFI+PgplbmRvYmoKNCAwIG9iago8PC9MZW5ndGggMzY+PgpzdHJlYW0KQlQKL0YxIDI0IFRmCjEwMCA3MDAgVGQKKChVU1BTIExhYmVsKSBUIDAKRVQKZW5kc3RyZWFtCmVuZG9iagogdHJhaWxlcgo8PC9Sb290IDEgMCBSL1NpemUgNT4+CnN0YXJ0eHJlZgo2MzYKJSVFT0Y=");
        _server.Given(Request.Create().WithPath("/labels/domestic").UsingPost().WithHeader("Accept", MediaTypeNames.Application.Pdf))
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", MediaTypeNames.Application.Pdf)
                .WithHeader("X-Tracking-Number", "9400111899223857268499")
                .WithBody(pdfBytes));
    }

    [Fact]
    public async Task Addresses_Standardize_Works()
    {
        var sp = BuildServices(_server.Url!);
        var client = sp.GetRequiredService<IAddressesClient>();
        var res = await client.StandardizeAsync(new StandardizeAddressRequest
        {
            Addresses = new() { new AddressInput { AddressLine1 = "X", City = "Y", State = "ZZ" } }
        });
        res.Addresses.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ShippingOptions_Quote_Works()
    {
        var sp = BuildServices(_server.Url!);
        var client = sp.GetRequiredService<IShippingOptionsClient>();
        var res = await client.QuoteAsync(new ShippingOptionsQuoteRequest { OriginZip = "10001", DestinationZip = "94105", WeightOz = 8 });
        res.Options.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Labels_Pdf_Works()
    {
        var sp = BuildServices(_server.Url!);
        var client = sp.GetRequiredService<IDomesticLabelsClient>();
        var res = await client.CreateAsync(new DomesticLabelRequest
        {
            Service = "Ground Advantage",
            Format = LabelFormat.Pdf,
            WeightOz = 8,
            ShipFrom = new Usps.V3.Models.Labels.Address { Name = "A", AddressLine1 = "1", City = "X", State = "NY", ZipCode = "10001" },
            ShipTo = new Usps.V3.Models.Labels.Address { Name = "B", AddressLine1 = "2", City = "Y", State = "CA", ZipCode = "94105" }
        });
        res.ContentType.Should().Be(MediaTypeNames.Application.Pdf);
        res.Content.Should().NotBeEmpty();
        res.TrackingNumber.Should().NotBeNullOrEmpty();
    }

    private static ServiceProvider BuildServices(string baseUrl)
    {
        var sc = new ServiceCollection();
                      sc.AddUspsV3(opts =>
        {
            opts.BaseUrl = baseUrl.TrimEnd('/') + "/";
            opts.ClientId = string.Empty; // force FakeTokenClient
            opts.ClientSecret = string.Empty;
        });
        return sc.BuildServiceProvider();
    }

    public void Dispose() => _server.Stop();
}

