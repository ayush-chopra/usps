using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Usps.V3.Models.Labels;
using Usps.V3.Options;
using Usps.V3.Http;

namespace Usps.V3.Clients;

internal sealed class DomesticLabelsClient : IDomesticLabelsClient
{
    private readonly HttpClient _http;
    private readonly UspsOptions _opts;
    private readonly ILogger<DomesticLabelsClient> _logger;

    public DomesticLabelsClient(HttpClient http, IOptions<UspsOptions> options, ILogger<DomesticLabelsClient> logger)
    {
        _http = http;
        _opts = options.Value;
        _logger = logger;
    }

    public async Task<DomesticLabelResponse> CreateAsync(DomesticLabelRequest req, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _opts.Paths.LabelsDomestic)
        {
            Content = new StringContent(JsonSerializer.Serialize(req, Json.JsonOptions), Encoding.UTF8, MediaTypeNames.Application.Json)
        };

        var accept = req.Format switch
        {
            LabelFormat.Pdf => MediaTypeNames.Application.Pdf,
            LabelFormat.Svg => "image/svg+xml",
            LabelFormat.Zpl => MediaTypeNames.Text.Plain,
            _ => MediaTypeNames.Application.Pdf
        };
        request.Headers.TryAddWithoutValidation("Accept", accept);

        using var resp = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new UspsApiException(resp.StatusCode, "Label creation failed", responseBody: text);
        }

        var contentType = resp.Content.Headers.ContentType?.MediaType ?? accept;
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        var tracking = resp.Headers.TryGetValues("X-Tracking-Number", out var vals) ? vals.FirstOrDefault() ?? string.Empty : string.Empty;

        return new DomesticLabelResponse
        {
            TrackingNumber = tracking,
            ContentType = contentType,
            Content = bytes
        };
    }
}

