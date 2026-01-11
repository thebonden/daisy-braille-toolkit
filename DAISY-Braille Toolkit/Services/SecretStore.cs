using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DAISY_Braille_Toolkit.Services
{
    /// <summary>
    /// Lokal sikker opbevaring af API keys.
    /// - Lokal fil under %LOCALAPPDATA% (ikke i repo)
    /// - Krypteret med DPAPI (CurrentUser)
    /// - Kan også læse fra miljøvariabel: ELEVENLABS_API_KEY (har prioritet)
    /// </summary>
    public static class SecretStore
    {
        private const string AppFolderName = "DAISY-Braille-Toolkit";
        private const string SecretsFileName = "secrets.json";
        private const string ElevenLabsEnvVar = "ELEVENLABS_API_KEY";

        public enum ApiKeySource
        {
            None = 0,
            Environment = 1,
            LocalEncrypted = 2
        }

        private sealed class SecretsFile
        {
            public string? ApiKeyProtectedBase64 { get; set; }
            public DateTime SavedUtc { get; set; }
        }

        private static string SecretsPath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    AppFolderName);

                return Path.Combine(dir, SecretsFileName);
            }
        }

        /// <summary>
        /// Gemmer ElevenLabs API key krypteret lokalt (DPAPI CurrentUser).
        /// </summary>
        public static void SaveElevenLabsApiKey(string apiKey)
        {
            if (apiKey is null) throw new ArgumentNullException(nameof(apiKey));
            apiKey = apiKey.Trim();

            if (apiKey.Length == 0)
                throw new ArgumentException("API key må ikke være tom.", nameof(apiKey));

            var dir = Path.GetDirectoryName(SecretsPath)!;
            Directory.CreateDirectory(dir);

            var plainBytes = Encoding.UTF8.GetBytes(apiKey);
            var protectedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);

            var file = new SecretsFile
            {
                ApiKeyProtectedBase64 = Convert.ToBase64String(protectedBytes),
                SavedUtc = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SecretsPath, json, Encoding.UTF8);
        }

        /// <summary>
        /// Sletter den lokalt gemte key (hvis den findes). Bemærk: miljøvariabel påvirkes ikke.
        /// </summary>
        public static void DeleteElevenLabsApiKey()
        {
            if (File.Exists(SecretsPath))
                File.Delete(SecretsPath);
        }

        /// <summary>
        /// Henter ElevenLabs API key. Miljøvariabel (ELEVENLABS_API_KEY) har prioritet.
        /// </summary>
        public static bool TryGetElevenLabsApiKey(out string apiKey)
        {
            // 1) Env
            var env = Environment.GetEnvironmentVariable(ElevenLabsEnvVar);
            if (!string.IsNullOrWhiteSpace(env))
            {
                apiKey = env.Trim();
                return true;
            }

            // 2) Local
            if (TryReadLocal(out apiKey, out _))
                return true;

            apiKey = string.Empty;
            return false;
        }

        /// <summary>
        /// Returnerer et "hint" til UI: hvor key kommer fra, sidste 4 tegn, og gemt-tidspunkt (hvis lokalt).
        /// </summary>
        public static bool TryGetElevenLabsApiKeyStatus(out ApiKeySource source, out string tail, out DateTime? savedUtcUtc)
        {
            source = ApiKeySource.None;
            tail = string.Empty;
            savedUtcUtc = null;

            // 1) Env
            var env = Environment.GetEnvironmentVariable(ElevenLabsEnvVar);
            if (!string.IsNullOrWhiteSpace(env))
            {
                var key = env.Trim();
                source = ApiKeySource.Environment;
                tail = GetTail(key);
                return true;
            }

            // 2) Local
            if (TryReadLocal(out var local, out var savedUtc))
            {
                source = ApiKeySource.LocalEncrypted;
                tail = GetTail(local);
                savedUtcUtc = savedUtc;
                return true;
            }

            return false;
        }

        private static string GetTail(string key)
        {
            key = (key ?? string.Empty).Trim();
            if (key.Length <= 4) return key;
            return key[^4..];
        }

        private static bool TryReadLocal(out string apiKey, out DateTime savedUtcUtc)
        {
            apiKey = string.Empty;
            savedUtcUtc = default;

            if (!File.Exists(SecretsPath))
                return false;

            try
            {
                var json = File.ReadAllText(SecretsPath, Encoding.UTF8);
                var file = JsonSerializer.Deserialize<SecretsFile>(json);

                if (file is null || string.IsNullOrWhiteSpace(file.ApiKeyProtectedBase64))
                    return false;

                savedUtcUtc = file.SavedUtc;

                var protectedBytes = Convert.FromBase64String(file.ApiKeyProtectedBase64);
                var plainBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);

                apiKey = Encoding.UTF8.GetString(plainBytes).Trim();
                return !string.IsNullOrWhiteSpace(apiKey);
            }
            catch
            {
                // Hvis filen er korrupt, eller key ikke kan dekrypteres på denne maskine/bruger.
                return false;
            }
        }
    }
}
