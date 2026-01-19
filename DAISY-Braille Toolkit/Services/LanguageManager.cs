using System;
using System.Linq;
using System.Windows;

namespace DAISY_Braille_Toolkit.Services
{
    public static class LanguageManager
    {
        private const string EnUri = "Assets/Strings/Strings.en.xaml";
        private const string DaUri = "Assets/Strings/Strings.da.xaml";

        public static string CurrentLanguage { get; private set; } = "en";

        public static void Apply(string languageCode)
        {
            languageCode = (languageCode ?? "en").Trim().ToLowerInvariant();
            if (languageCode != "da") languageCode = "en";

            var uri = languageCode == "da" ? DaUri : EnUri;

            var app = System.Windows.Application.Current;
            if (app == null) return;

            var toRemove = app.Resources.MergedDictionaries
                .Where(d => d.Source != null &&
                            (d.Source.OriginalString.EndsWith(EnUri, StringComparison.OrdinalIgnoreCase) ||
                             d.Source.OriginalString.EndsWith(DaUri, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            foreach (var d in toRemove)
                app.Resources.MergedDictionaries.Remove(d);

            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(uri, UriKind.Relative)
            });

            CurrentLanguage = languageCode;
        }

        public static string T(string key, string fallback = "")
        {
            try
            {
                var v = System.Windows.Application.Current?.TryFindResource(key);
                return v as string ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }
    }
}
