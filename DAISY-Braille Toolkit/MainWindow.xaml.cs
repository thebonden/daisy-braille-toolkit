using System;
using System.Windows;

namespace DAISY_Braille_Toolkit
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            RefreshApiKeyStatus();
        }

        private void SaveApiKey_Click(object sender, RoutedEventArgs e)
        {
            var key = ApiKeyBox?.Password?.Trim();

            if (string.IsNullOrWhiteSpace(key))
            {
                System.Windows.MessageBox.Show("Indtast en API key.");
                return;
            }

            Services.SecretStore.SaveElevenLabsApiKey(key);

            // Fjern nøglen fra UI igen (undgå at den står på skærmen).
            if (ApiKeyBox != null) ApiKeyBox.Password = string.Empty;

            System.Windows.MessageBox.Show("API key er gemt lokalt (krypteret).");
            RefreshApiKeyStatus();
        }

        private void DeleteApiKey_Click(object sender, RoutedEventArgs e)
        {
            Services.SecretStore.DeleteElevenLabsApiKey();

            if (ApiKeyBox != null) ApiKeyBox.Password = string.Empty;

            System.Windows.MessageBox.Show("API key er slettet lokalt.");
            RefreshApiKeyStatus();
        }

        private void RefreshApiKeyStatus()
        {
            if (ApiKeyStatusText == null) return;

            if (Services.SecretStore.TryGetElevenLabsApiKeyStatus(
                out Services.SecretStore.ApiKeySource source,
                out string tail,
                out DateTime? savedUtc))
            {
                var tailText = string.IsNullOrWhiteSpace(tail) ? "" : $" – slutter på …{tail}";

                if (source == Services.SecretStore.ApiKeySource.Environment)
                {
                    ApiKeyStatusText.Text = $"API key sat via miljøvariabel (ELEVENLABS_API_KEY){tailText}";
                    return;
                }

                // Local encrypted
                if (savedUtc.HasValue)
                {
                    var savedLocal = savedUtc.Value.ToLocalTime();
                    ApiKeyStatusText.Text = $"API key gemt lokalt (krypteret){tailText} (gemt {savedLocal:dd-MM-yyyy HH:mm})";
                }
                else
                {
                    ApiKeyStatusText.Text = $"API key gemt lokalt (krypteret){tailText}";
                }

                return;
            }

            ApiKeyStatusText.Text = "Ingen API key sat";
        }
    }
}
