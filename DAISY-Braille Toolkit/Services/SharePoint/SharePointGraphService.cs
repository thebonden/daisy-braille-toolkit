using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DAISY_Braille_Toolkit.Services.SharePoint
{
    public sealed class SharePointGraphService : IDisposable
    {
        private readonly HttpClient _http;
        private readonly SharePointAuthService? _auth;
        private string? _accessToken;

        public SharePointGraphService(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken)) throw new ArgumentException("Access token is required", nameof(accessToken));

            _accessToken = accessToken;
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        /// <summary>
        /// Construct a Graph client that pulls tokens from the provided SharePointAuthService.
        /// The service is expected to have a cached account (silent token acquisition).
        /// </summary>
        public SharePointGraphService(SharePointAuthService authService)
        {
            _auth = authService ?? throw new ArgumentNullException(nameof(authService));
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public void Dispose() => _http.Dispose();

        public async Task<(string SiteId, string DisplayName, string WebUrl)> GetSiteByUrlAsync(Uri siteUrl)
        {
            // Path-based addressing: /sites/{hostname}:{server-relative-path}:
            var host = siteUrl.Host;
            var serverRelativePath = siteUrl.AbsolutePath;
            if (!serverRelativePath.StartsWith('/')) serverRelativePath = "/" + serverRelativePath;

            var endpoint = $"https://graph.microsoft.com/v1.0/sites/{host}:{serverRelativePath}:?$select=id,displayName,webUrl";
            var json = await GetStringAsync(endpoint).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var siteId = root.GetProperty("id").GetString() ?? "";
            var displayName = root.TryGetProperty("displayName", out var dn) ? (dn.GetString() ?? "") : "";
            var webUrl = root.TryGetProperty("webUrl", out var wu) ? (wu.GetString() ?? "") : "";

            if (string.IsNullOrWhiteSpace(siteId))
                throw new InvalidOperationException("Kunne ikke hente site-id via Microsoft Graph.");

            return (siteId, displayName, webUrl);
        }

        public async Task<IReadOnlyList<(string Id, string DisplayName)>> GetListsAsync(string siteId)
        {
            var endpoint = $"https://graph.microsoft.com/v1.0/sites/{Uri.EscapeDataString(siteId)}/lists?$select=id,displayName";
            var json = await GetStringAsync(endpoint).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            var value = doc.RootElement.GetProperty("value");

            var lists = new List<(string Id, string DisplayName)>();
            foreach (var el in value.EnumerateArray())
            {
                var id = el.GetProperty("id").GetString() ?? "";
                var name = el.TryGetProperty("displayName", out var dn) ? (dn.GetString() ?? "") : "";
                if (!string.IsNullOrWhiteSpace(id)) lists.Add((id, name));
            }

            return lists;
        }

        public async Task<string?> FindListIdByDisplayNameAsync(string siteId, string displayName)
        {
            var lists = await GetListsAsync(siteId).ConfigureAwait(false);
            var hit = lists.FirstOrDefault(l => string.Equals(l.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(hit.Id) ? null : hit.Id;
        }

        public async Task<(bool Ok, string Message)> TestReadAccessAsync(Uri siteUrl, string countersListName, string productionsListName)
        {
            var (siteId, displayName, webUrl) = await GetSiteByUrlAsync(siteUrl).ConfigureAwait(false);
            var lists = await GetListsAsync(siteId).ConfigureAwait(false);

            var hasCounters = !string.IsNullOrWhiteSpace(countersListName) && lists.Any(l => string.Equals(l.DisplayName, countersListName, StringComparison.OrdinalIgnoreCase));
            var hasProductions = !string.IsNullOrWhiteSpace(productionsListName) && lists.Any(l => string.Equals(l.DisplayName, productionsListName, StringComparison.OrdinalIgnoreCase));

            var sb = new StringBuilder();
            sb.AppendLine($"Site: {displayName}");
            sb.AppendLine($"WebUrl: {webUrl}");
            sb.AppendLine($"Lister fundet: {lists.Count}");
            sb.AppendLine($"- {countersListName}: {(hasCounters ? "OK" : "IKKE fundet")}");
            sb.AppendLine($"- {productionsListName}: {(hasProductions ? "OK" : "IKKE fundet")}");

            if (!hasCounters || !hasProductions)
                return (false, sb.ToString());

            return (true, sb.ToString());
        }

        public async Task<(bool Ok, string Message)> TestWriteAccessAsync(Uri siteUrl, string listDisplayName)
        {
            // Creates + deletes a single test item in the chosen list.
            var (siteId, _, _) = await GetSiteByUrlAsync(siteUrl).ConfigureAwait(false);
            var listId = await FindListIdByDisplayNameAsync(siteId, listDisplayName).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(listId))
                return (false, $"Listen '{listDisplayName}' blev ikke fundet på sitet.");

            // Create item
            var createEndpoint = $"https://graph.microsoft.com/v1.0/sites/{Uri.EscapeDataString(siteId)}/lists/{Uri.EscapeDataString(listId)}/items";
            var title = $"DBT_TEST_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            var payload = JsonSerializer.Serialize(new
            {
                fields = new Dictionary<string, object>
                {
                    ["Title"] = title
                }
            });

            var created = await SendJsonAsync(HttpMethod.Post, createEndpoint, payload).ConfigureAwait(false);
            using var createdDoc = JsonDocument.Parse(created);
            var itemId = createdDoc.RootElement.GetProperty("id").GetString();

            if (string.IsNullOrWhiteSpace(itemId))
                return (false, "Kunne ikke oprette test-item (mangler item id i svar).");

            // Delete item
            var deleteEndpoint = $"https://graph.microsoft.com/v1.0/sites/{Uri.EscapeDataString(siteId)}/lists/{Uri.EscapeDataString(listId)}/items/{Uri.EscapeDataString(itemId)}";
            await SendNoContentAsync(HttpMethod.Delete, deleteEndpoint).ConfigureAwait(false);

            return (true, $"Write-test OK. Oprettet og slettet item: {title}");
        }

        private async Task<string> GetStringAsync(string url)
        {
            await EnsureTokenAsync().ConfigureAwait(false);
            using var resp = await _http.GetAsync(url).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Graph GET fejlede ({(int)resp.StatusCode}): {body}");
            return body;
        }

        private async Task<string> SendJsonAsync(HttpMethod method, string url, string json)
        {
            await EnsureTokenAsync().ConfigureAwait(false);
            using var req = new HttpRequestMessage(method, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            using var resp = await _http.SendAsync(req).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Graph {method} fejlede ({(int)resp.StatusCode}): {body}");
            return body;
        }

        private async Task SendNoContentAsync(HttpMethod method, string url)
        {
            await EnsureTokenAsync().ConfigureAwait(false);
            using var req = new HttpRequestMessage(method, url);
            using var resp = await _http.SendAsync(req).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Graph {method} fejlede ({(int)resp.StatusCode}): {body}");
        }

        private async Task EnsureTokenAsync()
        {
            if (_auth is null) return;

            // Acquire silently – the UI layer should handle interactive login.
            var result = await _auth.AcquireTokenAsync(forceInteractive: false).ConfigureAwait(false);
            var token = result.AccessToken;
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Kunne ikke hente access token (tomt token).");

            if (string.Equals(token, _accessToken, StringComparison.Ordinal)) return;
            _accessToken = token;
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }
}
