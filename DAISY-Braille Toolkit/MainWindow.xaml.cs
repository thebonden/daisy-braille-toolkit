using System;
using System.Windows;
using DAISY_Braille_Toolkit.Services;

namespace DAISY_Braille_Toolkit
{
    public partial class MainWindow : Window
    {
        private readonly SecretStore _secrets = new();

        public MainWindow()
        {
            InitializeComponent();
            RefreshApiKeyStatus();
        }

        private void RefreshApiKeyStatus()
        {
            // Environment variable always wins (useful for CI/dev)
            var env = Environment.GetEnvironmentVariable(SecretStore.EnvVarName);
            if (!string.IsNullOrWhiteSpace(env))
            {
                ApiKeyStatusText.Text = "Bruger miljøvariabel (ELEVENLABS_API_KEY).";
                return;
            }

            ApiKeyStatusText.Text = _secrets.HasStoredApiKey()
                ? "API key gemt lokalt (krypteret)."
                : "Ingen API key gemt. Indtast og tryk 'Gem lokalt'.";
        }

        private void SaveApiKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var key = ApiKeyBox.Password;
                _secrets.SaveApiKey(key);
                ApiKeyBox.Clear();
                RefreshApiKeyStatus();
                AppendLog("API key gemt lokalt (krypteret).");
                MessageBox.Show("API key er gemt lokalt (krypteret).", "DAISY-Braille Toolkit",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Kunne ikke gemme API key:\n" + ex.Message, "DAISY-Braille Toolkit",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteApiKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _secrets.DeleteApiKey();
                RefreshApiKeyStatus();
                AppendLog("Lokal API key slettet.");
                MessageBox.Show("Lokal API key er slettet.", "DAISY-Braille Toolkit",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Kunne ikke slette API key:\n" + ex.Message, "DAISY-Braille Toolkit",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AppendLog(string message)
        {
            if (LogBox == null) return;
            LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
            LogBox.ScrollToEnd();
        }
    }
}
