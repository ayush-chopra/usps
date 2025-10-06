using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Usps.V3.Http;

namespace Usps.V3;

internal static class HttpClientJsonExtensions
{
    public static async Task<T> PostJsonAsync<T>(this HttpClient http, string path, object payload, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, Json.JsonOptions), Encoding.UTF8, MediaTypeNames.Application.Json)
        };
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        return await ReadJsonAsync<T>(resp, ct).ConfigureAwait(false);
    }

    public static async Task<T> GetJsonAsync<T>(this HttpClient http, string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        return await ReadJsonAsync<T>(resp, ct).ConfigureAwait(false);
    }

    public static async Task<T> ReadJsonAsync<T>(HttpResponseMessage resp, CancellationToken ct)
    {
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            string? code = null;
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("code", out var c)) code = c.GetString();
            }
            catch { /* ignore */ }
            throw new UspsApiException(resp.StatusCode, $"USPS API error {(int)resp.StatusCode}", code, text);
        }

        await using var s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync<T>(s, Json.JsonOptions, ct).ConfigureAwait(false);
        if (result is null) throw new InvalidOperationException("Deserialization returned null");
        return result;
    }
}

