using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Windows.Forms;
using DAISY_Braille_Toolkit.Models;
using DAISY_Braille_Toolkit.Services;

namespace DAISY_Braille_Toolkit;

public partial class MainWindow : Window
{
    private readonly JobStore _store = new();
    private readonly PipelineRunner _runner = new();
    private readonly SecretStore _secrets = new();


    private JobManifest? _job;

    public MainWindow()
    {
        InitializeComponent();
        RefreshApiKeyStatus();
        AppendLog("Klar.");
    }

    private void PickInput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Dokumenter (*.docx;*.txt)|*.docx;*.txt|Alle filer (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
            InputPathTextBox.Text = dlg.FileName;
    }

    private void PickOutput_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Vælg output-mappe"
        };

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            OutputFolderTextBox.Text = dlg.SelectedPath;
    }

    private void NewJob_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(InputPathTextBox.Text) || !File.Exists(InputPathTextBox.Text))
        {
            System.Windows.MessageBox.Show("Vælg en inputfil først.");
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputFolderTextBox.Text) || !Directory.Exists(OutputFolderTextBox.Text))
        {
            System.Windows.MessageBox.Show("Vælg en output-mappe først.");
            return;
        }

        var mode = (OutputMode)ModeCombo.SelectedIndex;
        var voiceId = VoiceIdTextBox.Text.Trim();

        // Metadata
        var title = TitleTextBox.Text.Trim();
        var author = AuthorTextBox.Text.Trim();

        _job = _store.CreateNew(InputPathTextBox.Text, OutputFolderTextBox.Text, mode, voiceId);
        _job.Title = string.IsNullOrWhiteSpace(title) ? null : title;
        _job.Author = string.IsNullOrWhiteSpace(author) ? null : author;
        _job.Tts!.Settings.ModelId = ((ComboBoxItem)ModelCombo.SelectedItem).Content?.ToString() ?? _job.Tts!.Settings.ModelId;
        _job.Tts!.Settings.OutputFormat = ((ComboBoxItem)FormatCombo.SelectedItem).Content?.ToString() ?? _job.Tts!.Settings.OutputFormat;
        if (int.TryParse(MaxCharsTextBox.Text.Trim(), out var maxChars) && maxChars > 100)
            _job.Tts!.Settings.MaxCharsPerSegment = maxChars;

        _store.Save(_job.OutputRoot, _job);
        AppendLog($"Nyt job: {_job.OutputRoot}");
        StatusText.Text = "Nyt job oprettet";
    }

    private void OpenJob_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Vælg en eksisterende job-mappe (den der indeholder job.json)"
        };

        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        _job = _store.Load(dlg.SelectedPath);
        AppendLog($"Job loaded: {_job.OutputRoot}");
        StatusText.Text = "Job loaded (resume)";

        // Vis input/output i UI (så man kan se hvad job'et er)
        InputPathTextBox.Text = _job.InputPath;
        OutputFolderTextBox.Text = Directory.GetParent(_job.OutputRoot)?.FullName ?? _job.OutputRoot;
        VoiceIdTextBox.Text = _job.ElevenLabsVoiceId;
        TitleTextBox.Text = _job.Title ?? "";
        AuthorTextBox.Text = _job.Author ?? "";

        // TTS settings
        if (_job.Tts is not null)
        {
            SelectComboByContent(ModelCombo, _job.Tts.Settings.ModelId);
            SelectComboByContent(FormatCombo, _job.Tts.Settings.OutputFormat);
            MaxCharsTextBox.Text = _job.Tts.Settings.MaxCharsPerSegment.ToString();
        }
        ModeCombo.SelectedIndex = (int)_job.Mode;
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_job is null)
        {
            System.Windows.MessageBox.Show("Opret et nyt job eller åbn et eksisterende job først.");
            return;
        }

        try
        {
            // Opdater job med UI-valg (så man kan ændre titel/voice/model uden at lave nyt job)
            _job.Title = string.IsNullOrWhiteSpace(TitleTextBox.Text) ? null : TitleTextBox.Text.Trim();
            _job.Author = string.IsNullOrWhiteSpace(AuthorTextBox.Text) ? null : AuthorTextBox.Text.Trim();
            _job.ElevenLabsVoiceId = VoiceIdTextBox.Text.Trim();
            _job.Tts ??= new TtsJobState();
            _job.Tts.Settings.ModelId = ((ComboBoxItem)ModelCombo.SelectedItem).Content?.ToString() ?? _job.Tts.Settings.ModelId;
            _job.Tts.Settings.OutputFormat = ((ComboBoxItem)FormatCombo.SelectedItem).Content?.ToString() ?? _job.Tts.Settings.OutputFormat;
            if (int.TryParse(MaxCharsTextBox.Text.Trim(), out var maxChars) && maxChars > 100)
                _job.Tts.Settings.MaxCharsPerSegment = maxChars;

            _store.Save(_job.OutputRoot, _job);

            var apiKey = string.IsNullOrWhiteSpace(ApiKeyBox.Password) ? null : ApiKeyBox.Password;

            var forceStart = StartStepCombo.SelectedIndex switch
            {
                0 => (PipelineStep?)null,                 // Auto resume
                1 => PipelineStep.Tts,
                2 => PipelineStep.DaisyBuild,
                3 => PipelineStep.PefBuild,
                4 => PipelineStep.IsoAndCsv,
                _ => null
            };

            Progress.Value = 0;

            await _runner.RunAsync(
                _job,
                forceStart,
                log: AppendLog,
                progress: (p, msg) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        Progress.Value = p * 100;
                        StatusText.Text = msg;
                    });
                },
                elevenLabsApiKey: apiKey);

            AppendLog("Færdig ✅");
        }
        catch
        {
            AppendLog("Stoppede pga fejl. Du kan trykke Continue igen (resume).\nSe job.json for detaljer.");
        }
    }

    private static void SelectComboByContent(System.Windows.Controls.ComboBox combo, string content)
    {
        foreach (var item in combo.Items)
        {
            if (item is System.Windows.Controls.ComboBoxItem cbi &&
                string.Equals(cbi.Content?.ToString(), content, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = cbi;
                return;
            }
        }
    }

    
    private void RefreshApiKeyStatus()
    {
        // Miljøvariabel vinder altid
        var env = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
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
            MessageBox.Show("API key er gemt lokalt (krypteret).");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Kunne ikke gemme API key:\n" + ex.Message);
        }
    }

    private void DeleteApiKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _secrets.DeleteApiKey();
            RefreshApiKeyStatus();
            MessageBox.Show("Lokal API key er slettet.");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Kunne ikke slette API key:\n" + ex.Message);
        }
    }

private void AppendLog(string line)
    {
        Dispatcher.Invoke(() =>
        {
            LogText.AppendText(line + Environment.NewLine);
            LogText.ScrollToEnd();
        });
    }
}
