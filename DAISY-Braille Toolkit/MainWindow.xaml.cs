using System.Windows;
using Microsoft.Win32;
using System.Windows.Forms;
using DAISY_Braille_Toolkit.Models;
using DAISY_Braille_Toolkit.Services;

namespace DAISY_Braille_Toolkit;

public partial class MainWindow : Window
{
    private readonly JobStore _store = new();
    private readonly PipelineRunner _runner = new();

    private JobManifest? _job;

    public MainWindow()
    {
        InitializeComponent();
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

        _job = _store.CreateNew(InputPathTextBox.Text, OutputFolderTextBox.Text, mode, voiceId);
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
                });

            AppendLog("Færdig ✅");
        }
        catch
        {
            AppendLog("Stoppede pga fejl. Du kan trykke Continue igen (resume).\nSe job.json for detaljer.");
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
