using System;
using System.Text.Json;
using FluentAssertions;
using Usps.V3.Models.Addresses;
using Usps.V3;
using Xunit;

public class SerializationTests
{
    [Fact]
    public void Address_Serialization_CamelCase_RoundTrip()
    {
        var input = new AddressInput
        {
            AddressLine1 = "1600 Amphitheatre Pkwy",
            City = "Mountain View",
            State = "CA",
            ZipCode = "94043"
        };

        var json = JsonSerializer.Serialize(input, Json.JsonOptions);
        json.Should().Contain("addressLine1");

        var roundTrip = JsonSerializer.Deserialize<AddressInput>(json, Json.JsonOptions);
        roundTrip.Should().NotBeNull();
        roundTrip!.AddressLine1.Should().Be(input.AddressLine1);
    }
}

