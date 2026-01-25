using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using DAISY_Braille_Toolkit.Models;
using DAISY_Braille_Toolkit.Services;

namespace DAISY_Braille_Toolkit
{
    public partial class MainWindow : Window
    {
        private readonly PipelineRunner _pipelineRunner = new();
        private CancellationTokenSource? _jobCts;
        private bool _jobRunning;

        private sealed class OutputModeItem
        {
            public OutputMode Mode { get; init; }
            public string Label { get; init; } = string.Empty;
            public override string ToString() => Label;
        }

        /// <summary>
        /// (Re)build the output mode combo with localized labels.
        /// Called on startup and when language changes.
        /// </summary>
        private void InitOutputModeCombo()
        {
            try
            {
                if (OutputModeCombo == null) return;

                var items = new[]
                {
                    new OutputModeItem { Mode = OutputMode.Both, Label = LanguageManager.T("Mode_Both", "DAISY + Braille") },
                    new OutputModeItem { Mode = OutputMode.DaisyOnly, Label = LanguageManager.T("Mode_DaisyOnly", "DAISY only") },
                    new OutputModeItem { Mode = OutputMode.BrailleOnly, Label = LanguageManager.T("Mode_BrailleOnly", "Braille (PEF) only") },
                };

                var selectedMode = (OutputModeCombo.SelectedItem as OutputModeItem)?.Mode ?? OutputMode.Both;
                OutputModeCombo.ItemsSource = items;
                OutputModeCombo.SelectedItem = items.FirstOrDefault(i => i.Mode == selectedMode) ?? items[0];

                if (CancelJobButton != null)
                    CancelJobButton.IsEnabled = false;

                if (JobProgress != null)
                    JobProgress.Value = 0;

                if (JobStatusText != null && string.IsNullOrWhiteSpace(JobStatusText.Text))
                    JobStatusText.Text = "";
            }
            catch
            {
                // Ignore
            }
        }

        private async void RunJob_Click(object sender, RoutedEventArgs e)
        {
            if (_jobRunning) return;

            _jobRunning = true;
            _jobCts = new CancellationTokenSource();

            try
            {
                SetRunUi(isRunning: true);

                var jobFolder = (JobFolderBox.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(jobFolder) || !Directory.Exists(jobFolder))
                {
                    System.Windows.MessageBox.Show(
                        LanguageManager.T("Msg_SelectValidJobFolder", "Select a valid job folder first."),
                        LanguageManager.T("Title_Info", "Info"));
                    return;
                }

                var sourcePath = (SourceFileBox.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    System.Windows.MessageBox.Show(
                        "Select a valid source file first.",
                        LanguageManager.T("Title_Info", "Info"));
                    return;
                }

                if (!SecretStore.TryGetElevenLabsApiKey(out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
                {
                    System.Windows.MessageBox.Show(
                        "ElevenLabs API key is missing. Save it in the Settings tab first.",
                        LanguageManager.T("Title_Info", "Info"));
                    return;
                }

                var selectedMode = (OutputModeCombo.SelectedItem as OutputModeItem)?.Mode ?? OutputMode.Both;

                // Prefer the currently selected voice/model; fall back to saved settings.
                var voiceId = (VoiceCombo.SelectedValue as string) ?? _settings.ElevenLabsVoiceId;
                if (string.IsNullOrWhiteSpace(voiceId))
                {
                    System.Windows.MessageBox.Show(
                        "Select a default voice first (Voices tab).",
                        LanguageManager.T("Title_Info", "Info"));
                    return;
                }

                var modelId = (ModelCombo.SelectedItem as string) ?? _settings.ElevenLabsModelId;
                if (string.IsNullOrWhiteSpace(modelId)) modelId = "eleven_multilingual_v2";

                var store = new JobStore();
                JobManifest job;

                var manifestPath = JobStore.ManifestPath(jobFolder);
                if (File.Exists(manifestPath))
                {
                    job = store.Load(jobFolder);
                    AppendLog("Loaded existing job: " + manifestPath);
                }
                else
                {
                    job = store.CreateInFolder(jobFolder, sourcePath, selectedMode, voiceId);
                    job.Title = Path.GetFileNameWithoutExtension(sourcePath);
                    job.Tts ??= new TtsJobState();
                    job.Tts.Settings.ModelId = modelId;
                    job.Tts.Settings.MaxCharsPerSegment = TextSegmenter.GetSafeMaxChars(modelId);
                    store.Save(jobFolder, job);
                    AppendLog("Created job.json: " + manifestPath);
                }

                // Make sure the job uses the latest selected voice/model when resuming.
                // Job completion is tracked per-step (JobManifest.Steps), so we always refresh these.
                job.ElevenLabsVoiceId = voiceId;
                job.Tts ??= new TtsJobState();
                job.Tts.Settings.ModelId = modelId;
                job.Tts.Settings.MaxCharsPerSegment = TextSegmenter.GetSafeMaxChars(modelId);
                store.Save(jobFolder, job);

                // Run pipeline with progress updates
                JobStatusText.Text = "Starting...";
                JobProgress.Value = 0;

                await _pipelineRunner.RunAsync(
                    job,
                    forceStartAt: null,
                    log: AppendLog,
                    progress: (pct, msg) => Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            JobProgress.Value = Math.Max(0, Math.Min(100, pct * 100.0));
                            JobStatusText.Text = msg;
                        }
                        catch { }
                    }),
                    elevenLabsApiKey: apiKey,
                    ct: _jobCts.Token
                );

                JobStatusText.Text = "Done.";
            }
            catch (OperationCanceledException)
            {
                AppendLog("Job cancelled.");
                JobStatusText.Text = "Cancelled.";
            }
            catch (Exception ex)
            {
                AppendLog("Run job error: " + ex);
                JobStatusText.Text = "Failed: " + ex.Message;
                System.Windows.MessageBox.Show(ex.Message, LanguageManager.T("Title_Error", "Error"));
            }
            finally
            {
                try { _jobCts?.Dispose(); } catch { }
                _jobCts = null;
                _jobRunning = false;
                SetRunUi(isRunning: false);
            }
        }

        private void CancelJob_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _jobCts?.Cancel();
            }
            catch { }
        }

        private void SetRunUi(bool isRunning)
        {
            try
            {
                if (CancelJobButton != null)
                    CancelJobButton.IsEnabled = isRunning;

                if (OutputModeCombo != null)
                    OutputModeCombo.IsEnabled = !isRunning;
            }
            catch { }
        }
    }
}
