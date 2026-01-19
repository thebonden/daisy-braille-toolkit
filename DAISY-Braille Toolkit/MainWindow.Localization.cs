using System;
using System.Windows;
using System.Windows.Controls;
using DAISY_Braille_Toolkit.Services;

namespace DAISY_Braille_Toolkit
{
    public partial class MainWindow : Window
    {
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateLanguageMenuChecks();
        }

        private void SetEnglish_Click(object sender, RoutedEventArgs e)
        {
            ApplyAndPersistLanguage("en");
        }

        private void SetDanish_Click(object sender, RoutedEventArgs e)
        {
            ApplyAndPersistLanguage("da");
        }

        private void ApplyAndPersistLanguage(string lang)
        {
            LanguageManager.Apply(lang);

            try
            {
                var store = new AppSettingsStore();
                var settings = store.Load();
                settings.UiLanguage = lang;
                store.Save(settings);
            }
            catch
            {
                // Ignore persistence errors; language still changes for this session.
            }

            UpdateLanguageMenuChecks();
        }

        private void UpdateLanguageMenuChecks()
        {
            try
            {
                var en = FindName("EnglishMenuItem") as MenuItem;
                var da = FindName("DanishMenuItem") as MenuItem;

                if (en != null) en.IsChecked = LanguageManager.CurrentLanguage == "en";
                if (da != null) da.IsChecked = LanguageManager.CurrentLanguage == "da";
            }
            catch
            {
                // Menu items not available yet (e.g., if called too early)
            }
        }
    }
}
