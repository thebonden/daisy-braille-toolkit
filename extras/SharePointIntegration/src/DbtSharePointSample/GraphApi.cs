using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DbtSharePointSample;

public sealed class GraphApi
{
    private readonly HttpClient _http;

    public GraphApi(HttpClient http) => _http = http;

    public async Task<JsonDocument> GetAsync(string url, string accessToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var res = await _http.SendAsync(req).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
            throw new Exception($"GET {url} failed {res.StatusCode}: {body}");

        return JsonDocument.Parse(body);
    }

    public async Task<JsonDocument> PostJsonAsync(string url, string accessToken, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var res = await _http.SendAsync(req).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
            throw new Exception($"POST {url} failed {res.StatusCode}: {body}");

        return JsonDocument.Parse(body);
    }

    public async Task<JsonDocument> PatchJsonAsync(string url, string accessToken, object payload, string? ifMatchEtag = null)
    {
        var json = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(new HttpMethod("PATCH"), url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(ifMatchEtag))
            req.Headers.TryAddWithoutValidation("If-Match", ifMatchEtag);

        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var res = await _http.SendAsync(req).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"PATCH {url} failed {res.StatusCode}: {body}", null, res.StatusCode);

        return JsonDocument.Parse(body);
    }
}
