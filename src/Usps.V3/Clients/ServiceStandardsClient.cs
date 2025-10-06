using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Usps.V3.Models.ServiceStandards;
using Usps.V3.Options;

namespace Usps.V3.Clients;

internal sealed class ServiceStandardsClient : IServiceStandardsClient
{
    private readonly HttpClient _http;
    private readonly UspsOptions _opts;
    private readonly ILogger<ServiceStandardsClient> _logger;

    public ServiceStandardsClient(HttpClient http, IOptions<Usps.V3.Options.UspsOptions> options, ILogger<ServiceStandardsClient> logger)
    {
        _http = http;
        _opts = options.Value;
        _logger = logger;
    }

    public Task<ServiceStandardsLookupResponse> LookupAsync(ServiceStandardsLookupRequest req, CancellationToken ct = default)
    {
        _logger.LogInformation("Service standards for {Service} {Origin}->{Dest}", req.Service, req.OriginZip, req.DestinationZip);
        return _http.PostJsonAsync<ServiceStandardsLookupResponse>(_opts.Paths.ServiceStandardsLookup, req, ct);
    }

    public Task<ServiceStandardsFilesResponse> GetFilesAsync(CancellationToken ct = default)
        => _http.GetJsonAsync<ServiceStandardsFilesResponse>(_opts.Paths.ServiceStandardsFiles, ct);
}

