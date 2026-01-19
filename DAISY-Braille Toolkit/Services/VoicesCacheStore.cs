using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DAISY_Braille_Toolkit.Models;

namespace DAISY_Braille_Toolkit.Services
{
    public sealed class VoicesCache
    {
        public DateTime SyncedUtc { get; set; } = DateTime.UtcNow;
        public List<VoiceInfo> Voices { get; set; } = new();
    }

    public sealed class VoicesCacheStore
    {
        private const string AppFolderName = "DAISY-Braille-Toolkit";
        private const string CacheFileName = "voices.json";

        private static string CachePath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    AppFolderName);
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, CacheFileName);
            }
        }

        public VoicesCache? Load()
        {
            if (!File.Exists(CachePath))
                return null;

            var json = File.ReadAllText(CachePath);
            return JsonSerializer.Deserialize<VoicesCache>(json);
        }

        public void Save(VoicesCache cache)
        {
            var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(CachePath, json);
        }
    }
}
