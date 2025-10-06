using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using Usps.V3.Auth;
using Usps.V3.Clients;
using Usps.V3.Http;
using Usps.V3.Options;

namespace Usps.V3;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUspsV3(this IServiceCollection services, Action<UspsOptions>? configure = null)
    {
        services.AddOptions<UspsOptions>();
        if (configure != null)
            services.PostConfigure(configure);

        services.AddMemoryCache();

        services.AddTransient<AuthDelegatingHandler>();
        services.AddTransient<Http.CorrelationIdHandler>();

        // Decide token client at runtime (Fake when creds missing or MOCK_SERVER_BASEURL set)
        services.AddHttpClient<IUspsTokenClient, UspsTokenClient>();
        services.AddSingleton<IUspsTokenClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<UspsOptions>>().Value;
            opts.ApplyEnvironmentDefaults();

            var mockBase = Environment.GetEnvironmentVariable("MOCK_SERVER_BASEURL");
            if (!string.IsNullOrWhiteSpace(mockBase))
            {
                opts.BaseUrl = mockBase!.TrimEnd('/') + "/";
            }

            var logger = sp.GetRequiredService<ILogger<UspsTokenClient>>();
            if (string.IsNullOrWhiteSpace(opts.ClientId) || string.IsNullOrWhiteSpace(opts.ClientSecret) || !string.IsNullOrWhiteSpace(mockBase))
            {
                logger.LogInformation("Registering FakeTokenClient (no credentials or mock base url)");
                return new FakeTokenClient(sp.GetRequiredService<ILogger<FakeTokenClient>>());
            }

            logger.LogInformation("Registering UspsTokenClient (production token)");
            return ActivatorUtilities.CreateInstance<UspsTokenClient>(sp);
        });

        void AddClient<TClient, TImpl>() where TClient : class where TImpl : class, TClient
        {
            services.AddHttpClient<TClient, TImpl>()
            .ConfigureHttpClient((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<UspsOptions>>().Value;
                opts.ApplyEnvironmentDefaults();
                var mockBase = Environment.GetEnvironmentVariable("MOCK_SERVER_BASEURL");
                if (!string.IsNullOrWhiteSpace(mockBase))
                {
                    opts.BaseUrl = mockBase!.TrimEnd('/') + "/";
                }
                client.BaseAddress = new Uri(opts.BaseUrl, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
            })
            .AddHttpMessageHandler<AuthDelegatingHandler>()
            .AddHttpMessageHandler<Http.CorrelationIdHandler>()
            .AddPolicyHandler(PollyPolicies.Default());
        }

        AddClient<IAddressesClient, AddressesClient>();
        AddClient<IDomesticPricesClient, DomesticPricesClient>();
        AddClient<IInternationalPricesClient, InternationalPricesClient>();
        AddClient<IServiceStandardsClient, ServiceStandardsClient>();
        AddClient<IDomesticLabelsClient, DomesticLabelsClient>();
        AddClient<IShippingOptionsClient, ShippingOptionsClient>();

        return services;
    }
}

