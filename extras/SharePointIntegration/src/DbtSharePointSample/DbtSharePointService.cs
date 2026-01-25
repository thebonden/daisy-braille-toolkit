using System.Net;

namespace DbtSharePointSample;

public sealed class DbtSharePointService
{
    private readonly GraphApi _graph;
    private readonly AuthService _auth;
    private readonly AppConfig _cfg;

    private string? _siteId;
    private Dictionary<string, string>? _listIds;

    public DbtSharePointService(GraphApi graph, AuthService auth, AppConfig cfg)
    {
        _graph = graph;
        _auth = auth;
        _cfg = cfg;
    }

    public async Task TestConnectionAsync()
    {
        var token = await _auth.AcquireAccessTokenAsync();
        var (host, sitePath) = ParseSiteUrl(_cfg.SiteUrl);

        var siteDoc = await _graph.GetAsync($"https://graph.microsoft.com/v1.0/sites/{host}:{sitePath}?$select=id,webUrl", token);
        _siteId = siteDoc.RootElement.GetProperty("id").GetString() ?? throw new Exception("site id missing");
        Console.WriteLine($"   Site OK: {_cfg.SiteUrl}");
        Console.WriteLine($"   siteId: {_siteId}");

        var listsDoc = await _graph.GetAsync($"https://graph.microsoft.com/v1.0/sites/{_siteId}/lists?$select=id,displayName", token);
        _listIds = listsDoc.RootElement.GetProperty("value")
            .EnumerateArray()
            .Where(x => x.TryGetProperty("displayName", out _))
            .ToDictionary(
                x => x.GetProperty("displayName").GetString()!,
                x => x.GetProperty("id").GetString()!
            );

        foreach (var name in new[]
        {
            _cfg.Lists.Counters, _cfg.Lists.Productions, _cfg.Lists.ProducedFor, _cfg.Lists.ProducedFrom, _cfg.Lists.ReturnAddress, _cfg.Lists.EmployeeAbbrev
        })
        {
            if (!_listIds.ContainsKey(name))
                throw new Exception($"Missing list '{name}' on site. Create lists first (provisioning module) or fix list names in appsettings.json.");
        }

        Console.WriteLine("   Lists OK: found required lists.");
    }

    public async Task<ReserveResult> ReserveProductionAsync(string producedForCode)
    {
        await EnsureResolvedAsync();
        var token = await _auth.AcquireAccessTokenAsync();

        var dateKey = DateTime.Now.ToString(_cfg.DateKeyFormat);
        var countersListId = _listIds![_cfg.Lists.Counters];

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            var filter = Uri.EscapeDataString($"fields/DateKey eq '{dateKey}'");
            var url = $"https://graph.microsoft.com/v1.0/sites/{_siteId}/lists/{countersListId}/items?$top=1&$expand=fields($select=DateKey,Prefix,NextNumber)&$filter={filter}";
            var doc = await _graph.GetAsync(url, token);

            var items = doc.RootElement.GetProperty("value").EnumerateArray().ToList();
            if (items.Count == 0)
            {
                try
                {
                    var create = new
                    {
                        fields = new Dictionary<string, object>
                        {
                            ["DateKey"] = dateKey,
                            ["Prefix"] = producedForCode,
                            ["NextNumber"] = 2
                        }
                    };
                    await _graph.PostJsonAsync($"https://graph.microsoft.com/v1.0/sites/{_siteId}/lists/{countersListId}/items", token, create);

                    var vol = BuildVolumeLabel(producedForCode, dateKey, 1, _cfg.SequencePadding);
                    return new ReserveResult(dateKey, 1, vol, producedForCode);
                }
                catch (Exception)
                {
                    Console.WriteLine($"   [attempt {attempt}] counter create raced, retrying...");
                    await Task.Delay(150);
                    continue;
                }
            }
            else
            {
                var item = items[0];
                var itemId = item.GetProperty("id").GetString() ?? throw new Exception("counter item id missing");
                var etag = item.TryGetProperty("@odata.etag", out var et) ? et.GetString() : null;

                var fields = item.GetProperty("fields");
                var next = fields.GetProperty("NextNumber").GetInt32();
                var reserve = next;
                var updatedNext = next + 1;

                try
                {
                    var patchUrl = $"https://graph.microsoft.com/v1.0/sites/{_siteId}/lists/{countersListId}/items/{itemId}/fields";
                    var payload = new Dictionary<string, object> { ["NextNumber"] = updatedNext };
                    await _graph.PatchJsonAsync(patchUrl, token, payload, etag);

                    var vol = BuildVolumeLabel(producedForCode, dateKey, reserve, _cfg.SequencePadding);
                    return new ReserveResult(dateKey, reserve, vol, producedForCode);
                }
                catch (HttpRequestException hre) when (hre.StatusCode == HttpStatusCode.PreconditionFailed)
                {
                    Console.WriteLine($"   [attempt {attempt}] ETag changed (412), retrying...");
                    await Task.Delay(150);
                    continue;
                }
            }
        }

        throw new Exception("Failed to reserve a production number after multiple attempts.");
    }

    public async Task CreateProductionAsync(ReserveResult r)
    {
        await EnsureResolvedAsync();
        var token = await _auth.AcquireAccessTokenAsync();

        var productionsListId = _listIds![_cfg.Lists.Productions];

        var create = new
        {
            fields = new Dictionary<string, object>
            {
                ["VolumeLabel"] = r.VolumeLabel,
                ["Prefix"] = r.ProducedForCode,
                ["DateKey"] = r.DateKey,
                ["Sequence"] = r.Sequence,
                ["Status"] = "Reserved",
                ["ReservedAt"] = DateTime.UtcNow.ToString("o")
            }
        };

        await _graph.PostJsonAsync($"https://graph.microsoft.com/v1.0/sites/{_siteId}/lists/{productionsListId}/items", token, create);
    }

    private async Task EnsureResolvedAsync()
    {
        if (_siteId is not null && _listIds is not null) return;
        await TestConnectionAsync();
    }

    private static string BuildVolumeLabel(string code, string dateKey, int sequence, int pad)
        => $"{code}_{dateKey}_{sequence.ToString().PadLeft(pad, '0')}";

    private static (string host, string sitePath) ParseSiteUrl(string siteUrl)
    {
        var uri = new Uri(siteUrl);
        var host = uri.Host;
        var path = uri.AbsolutePath.TrimEnd('/');

        if (!path.StartsWith("/sites/", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/teams/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("SiteUrl must be https://<tenant>.sharepoint.com/sites/<name> (or /teams/<name>)");

        return (host, path);
    }
}
