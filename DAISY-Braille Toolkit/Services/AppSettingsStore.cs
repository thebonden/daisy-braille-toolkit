using System;
using System.IO;
using System.Text.Json;
using DAISY_Braille_Toolkit.Models;

namespace DAISY_Braille_Toolkit.Services
{
    public sealed class AppSettingsStore
    {
        private const string AppFolderName = "DAISY-Braille-Toolkit";
        private const string SettingsFileName = "settings.json";

        private static string SettingsPath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    AppFolderName);
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, SettingsFileName);
            }
        }

        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new AppSettings();

                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
    }
}
