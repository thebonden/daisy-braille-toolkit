using System.Windows;
using WinForms = System.Windows.Forms;

namespace DAISY_Braille_Toolkit;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        RefreshApiKeyStatus();
    }

    private void SaveApiKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var key = ApiKeyBox?.Password?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                System.Windows.MessageBox.Show("API key er tom.", "DAISY-Braille Toolkit",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Services.SecretStore.SaveElevenLabsApiKey(key);
            RefreshApiKeyStatus();

            System.Windows.MessageBox.Show("API key er gemt lokalt (krypteret).", "DAISY-Braille Toolkit",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Fejl",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteApiKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Services.SecretStore.DeleteElevenLabsApiKey();
            RefreshApiKeyStatus();

            System.Windows.MessageBox.Show("Lokal API key er slettet.", "DAISY-Braille Toolkit",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Fejl",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshApiKeyStatus()
    {
        var hasKey = Services.SecretStore.TryGetElevenLabsApiKey(out _);
        if (ApiKeyStatusText != null)
        {
            ApiKeyStatusText.Text = hasKey
                ? "API key gemt lokalt (krypteret)"
                : "Ingen API key gemt (klik Gem lokalt)";
        }
    }

    // Optional: folder picker helper you can wire to a button later.
    private string? PickFolder(string description)
    {
        using var dlg = new WinForms.FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        return dlg.ShowDialog() == WinForms.DialogResult.OK ? dlg.SelectedPath : null;
    }
}
