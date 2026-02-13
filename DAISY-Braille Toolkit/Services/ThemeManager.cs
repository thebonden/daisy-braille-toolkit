using System;
using System.Linq;
using System.Windows;

namespace DAISY_Braille_Toolkit.Services
{
    public static class ThemeManager
    {
        private const string ThemePrefix = "Assets/Themes/Theme.";

        public static void ApplyTheme(string? themeMode)
        {
            var app = System.Windows.Application.Current;
            if (app == null)
                return;

            var dictionaries = app.Resources.MergedDictionaries;
            var existing = dictionaries.FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains(ThemePrefix, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                dictionaries.Remove(existing);

            var normalized = NormalizeTheme(themeMode);
            var source = new Uri($"{ThemePrefix}{normalized}.xaml", UriKind.Relative);
            dictionaries.Insert(0, new ResourceDictionary { Source = source });
        }

        public static string NormalizeTheme(string? themeMode)
        {
            if (string.IsNullOrWhiteSpace(themeMode))
                return "System";

            var mode = themeMode.Trim();
            if (string.Equals(mode, "Dark", StringComparison.OrdinalIgnoreCase))
                return "Dark";
            if (string.Equals(mode, "Light", StringComparison.OrdinalIgnoreCase))
                return "Light";
            if (string.Equals(mode, "HighContrast", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "High Contrast", StringComparison.OrdinalIgnoreCase))
                return "HighContrast";

            return "System";
        }
    }
}
