using System;
using System.Globalization;
using System.Windows;
using DAISY_Braille_Toolkit.Services;

namespace DAISY_Braille_Toolkit
{
    public partial class App : System.Windows.Application
    {
        private void App_Startup(object sender, StartupEventArgs e)
        {
            // Load UI language from settings (stored in the same settings.json as other app settings)
            var lang = LoadSavedUiLanguage();

            // Set culture (numbers/dates) to match language. This is independent of the UI string resources.
            try
            {
                var culture = lang == "da" ? new CultureInfo("da-DK") : new CultureInfo("en-US");
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
            }
            catch
            {
                // Ignore culture errors; the app can still run.
            }

            // Apply string resources
            LanguageManager.Apply(lang);
        }

        private static string LoadSavedUiLanguage()
        {
            try
            {
                var store = new AppSettingsStore();
                var settings = store.Load();

                var lang = NormalizeLanguage(settings.UiLanguage);
                return lang;
            }
            catch
            {
                return "en";
            }
        }

        private static string NormalizeLanguage(string? lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return "en";

            lang = lang.Trim().ToLowerInvariant();

            // Accept common variants
            if (lang.StartsWith("da")) return "da";
            if (lang.StartsWith("en")) return "en";

            return "en";
        }
    }
}
