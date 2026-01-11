using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DAISY_Braille_Toolkit.Services;

public sealed class SecretStore
{
    public const string EnvVarName = "ELEVENLABS_API_KEY";

    private static string BaseDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DAISY-Braille-Toolkit");

    private static string SecretsPath => Path.Combine(BaseDir, "secrets.json");

    private sealed class SecretsFile
    {
        public string? ApiKeyProtectedBase64 { get; set; }
        public DateTime SavedUtc { get; set; }
    }

    /// <summary>
    /// Gets the API key. Priority:
    /// 1) Environment variable ELEVENLABS_API_KEY
    /// 2) Local encrypted secrets file (DPAPI, CurrentUser)
    /// </summary>
    public string? GetApiKey()
    {
        var fromEnv = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv.Trim();

        if (!File.Exists(SecretsPath))
            return null;

        try
        {
            var json = File.ReadAllText(SecretsPath);
            var secrets = JsonSerializer.Deserialize<SecretsFile>(json);
            if (string.IsNullOrWhiteSpace(secrets?.ApiKeyProtectedBase64))
                return null;

            var protectedBytes = Convert.FromBase64String(secrets.ApiKeyProtectedBase64);
            var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    public bool HasStoredApiKey()
    {
        if (!File.Exists(SecretsPath))
            return false;

        try
        {
            var json = File.ReadAllText(SecretsPath);
            var secrets = JsonSerializer.Deserialize<SecretsFile>(json);
            return !string.IsNullOrWhiteSpace(secrets?.ApiKeyProtectedBase64);
        }
        catch
        {
            return false;
        }
    }

    public void SaveApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key må ikke være tom.", nameof(apiKey));

        Directory.CreateDirectory(BaseDir);

        var bytes = Encoding.UTF8.GetBytes(apiKey.Trim());
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);

        var secrets = new SecretsFile
        {
            ApiKeyProtectedBase64 = Convert.ToBase64String(protectedBytes),
            SavedUtc = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(secrets, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SecretsPath, json);
    }

    public void DeleteApiKey()
    {
        if (File.Exists(SecretsPath))
            File.Delete(SecretsPath);
    }
}
