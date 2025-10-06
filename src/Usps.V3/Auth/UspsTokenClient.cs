using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Usps.V3.Options;

namespace Usps.V3.Auth;

internal sealed class UspsTokenClient : IUspsTokenClient
{
    private readonly HttpClient _http;
    private readonly ILogger<UspsTokenClient> _logger;
    private readonly IOptions<UspsOptions> _options;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _expiresAt;

    public UspsTokenClient(HttpClient http, IOptions<UspsOptions> options, ILogger<UspsTokenClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(_cachedToken) && DateTimeOffset.UtcNow < _expiresAt)
        {
            return _cachedToken!;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrEmpty(_cachedToken) && DateTimeOffset.UtcNow < _expiresAt)
            {
                return _cachedToken!;
            }

            var opts = _options.Value;
            var body = new
            {
                grant_type = "client_credentials",
                client_id = opts.ClientId,
                client_secret = opts.ClientSecret
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, opts.OAuthTokenUrl)
            {
                Content = JsonContent.Create(body, options: Json.JsonOptions)
            };

            var start = DateTimeOffset.UtcNow;
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var duration = DateTimeOffset.UtcNow - start;

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("USPS token request failed: {Status} in {Duration}ms", (int)resp.StatusCode, duration.TotalMilliseconds);
                var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new HttpRequestException($"Token request failed with {(int)resp.StatusCode}: {text}");
            }

            using var s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var doc = await JsonDocument.ParseAsync(s, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;
            var token = root.TryGetProperty("access_token", out var tokenEl) ? tokenEl.GetString() : null;
            var expiresIn = root.TryGetProperty("expires_in", out var expEl) ? expEl.GetInt32() : 3600;
            if (string.IsNullOrEmpty(token))
            {
                throw new InvalidOperationException("USPS OAuth response missing access_token");
            }

            var lifetime = TimeSpan.FromSeconds(expiresIn);
            var refreshAt = DateTimeOffset.UtcNow.Add(lifetime * 0.7);

            _cachedToken = token;
            _expiresAt = refreshAt;
            _logger.LogInformation("USPS token acquired, will refresh at {RefreshAt}", _expiresAt);
            return token!;
        }
        finally
        {
            _gate.Release();
        }
    }
}

