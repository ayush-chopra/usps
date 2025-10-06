using Polly;
using Polly.Extensions.Http;

namespace Usps.V3.Http;

public static class PollyPolicies
{
    public static IAsyncPolicy<HttpResponseMessage> Default()
    {
        var retry = HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => (int)r.StatusCode == 429)
            .WaitAndRetryAsync(5, retryAttempt =>
                TimeSpan.FromMilliseconds(200 * Math.Pow(2, retryAttempt)) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250))
            );

        return retry;
    }
}

