using System;
using System.Threading.Tasks;
using System.Windows;

using DAISY_Braille_Toolkit.Services.SharePoint;

namespace DAISY_Braille_Toolkit;

public partial class SharePointAuthWindow : Window
{
    private SharePointAuthConfig _config;
    private SharePointAuthService _authService;
    private SharePointGraphService _graphService;

    public SharePointAuthWindow(SharePointAuthConfig config)
    {
        InitializeComponent();

        _config = config;
        _authService = new SharePointAuthService(_config);
        _graphService = new SharePointGraphService(_authService);

        // Pre-fill UI
        TenantIdBox.Text = _config.TenantId;
        ClientIdBox.Text = _config.ClientId;
        SiteUrlBox.Text = _config.SiteUrl;
        CountersListBox.Text = _config.CountersListName;
        ProductionsListBox.Text = _config.ProductionsListName;

        UpdateUi();
    }

    private void Log(string message)
    {
        OutputBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        OutputBox.ScrollToEnd();
    }

    private void EnsureServices(SharePointAuthConfig cfg)
    {
        // Recreate services if the user changed settings in the UI.
        if (cfg != _config)
        {
            _config = cfg;
            _authService = new SharePointAuthService(_config);
            _graphService = new SharePointGraphService(_authService);
        }
    }

    private void UpdateUi()
    {
        var user = _authService.SignedInAccountUsername;
        LoginButton.IsEnabled = true;
        LogoutButton.IsEnabled = !string.IsNullOrWhiteSpace(user);
        TestReadButton.IsEnabled = !string.IsNullOrWhiteSpace(user);
        TestWriteButton.IsEnabled = !string.IsNullOrWhiteSpace(user);

        if (!string.IsNullOrWhiteSpace(user))
        {
            Log($"Signed in as: {user}");
        }
    }

    private SharePointAuthConfig ReadConfigFromUi()
    {
        // Keep any optional scopes passed from the caller.
        return new SharePointAuthConfig(
            TenantIdBox.Text.Trim(),
            ClientIdBox.Text.Trim(),
            SiteUrlBox.Text.Trim(),
            CountersListBox.Text.Trim(),
            ProductionsListBox.Text.Trim(),
            _config.Scopes
        );
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // If the user edits fields, recreate services using updated values.
            var cfg = ReadConfigFromUi();
            EnsureServices(cfg);

            Log("Attempting sign-in (silent first, then interactive if required)...");
            await _authService.AcquireTokenAsync();
            Log("Token acquired.");
            UpdateUi();
        }
        catch (Exception ex)
        {
            Log($"Login failed: {ex.Message}");
            System.Windows.MessageBox.Show(this, ex.Message, "SharePoint sign-in failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _authService.SignOutAsync();
            Log("Signed out.");
            UpdateUi();
        }
        catch (Exception ex)
        {
            Log($"Logout failed: {ex.Message}");
            System.Windows.MessageBox.Show(this, ex.Message, "SharePoint sign-out failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void TestRead_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var cfg = ReadConfigFromUi();
            Log("Testing READ access to SharePoint lists...");

            await _graphService.TestReadAccessAsync(new Uri(cfg.SiteUrl), cfg.CountersListName, cfg.ProductionsListName);
            Log("READ test completed (success).");
        }
        catch (Exception ex)
        {
            Log($"READ test failed: {ex.Message}");
            System.Windows.MessageBox.Show(this, ex.Message, "SharePoint read test failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void TestWrite_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var cfg = ReadConfigFromUi();
            Log("Testing WRITE access to SharePoint Productions list...");

            await _graphService.TestWriteAccessAsync(new Uri(cfg.SiteUrl), cfg.ProductionsListName);
            Log("WRITE test completed (success).");
        }
        catch (Exception ex)
        {
            Log($"WRITE test failed: {ex.Message}");
            System.Windows.MessageBox.Show(this, ex.Message, "SharePoint write test failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
