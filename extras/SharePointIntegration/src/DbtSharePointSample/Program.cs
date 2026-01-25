using System.Text.Json;

namespace DbtSharePointSample;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("DBT SharePoint sample (MSAL + Microsoft Graph)\n");

        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(configPath))
        {
            Console.WriteLine("Missing appsettings.json. Copy appsettings.example.json to appsettings.json and fill in TenantId, ClientId and SiteUrl.");
            return 1;
        }

        var cfg = JsonSerializer.Deserialize<AppConfig>(await File.ReadAllTextAsync(configPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new Exception("Invalid appsettings.json");

        var auth = new AuthService(cfg.TenantId, cfg.ClientId, cfg.Scopes);
        var graph = new GraphApi(new HttpClient());
        var dbt = new DbtSharePointService(graph, auth, cfg);

        Console.WriteLine("1) Testing connection...");
        await dbt.TestConnectionAsync();

        Console.WriteLine("\n2) Reserve production ID");
        Console.Write("Enter ProducedFor Code (e.g. DBS): ");
        var code = (Console.ReadLine() ?? "").Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            Console.WriteLine("No code entered. Exiting.");
            return 1;
        }

        var result = await dbt.ReserveProductionAsync(code);
        Console.WriteLine($"\nReserved:\n  DateKey: {result.DateKey}\n  Sequence: {result.Sequence}\n  VolumeLabel: {result.VolumeLabel}");

        Console.Write("\nCreate DBT_Productions entry now? (y/N): ");
        var yes = (Console.ReadLine() ?? "").Trim().Equals("y", StringComparison.OrdinalIgnoreCase);
        if (yes)
        {
            await dbt.CreateProductionAsync(result);
            Console.WriteLine("Created DBT_Productions item.");
        }
        else
        {
            Console.WriteLine("Skipped create.");
        }

        Console.WriteLine("\nDone.");
        return 0;
    }
}
