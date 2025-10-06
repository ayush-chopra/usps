using FluentAssertions;
using Polly;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Usps.V3.Http;
using Xunit;

public class PollyPolicyTests
{
    [Fact]
    public async Task DefaultPolicy_Retries_429_And_Succeeds()
    {
        var attempts = 0;
        var policy = PollyPolicies.Default();

        var result = await policy.ExecuteAsync(async () =>
        {
            attempts++;
            if (attempts < 3)
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests);
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        });

        result.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        attempts.Should().Be(3);
    }
}

