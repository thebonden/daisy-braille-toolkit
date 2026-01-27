using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DAISY_Braille_Toolkit.Models;
using DAISY_Braille_Toolkit.Services;
using WinForms = System.Windows.Forms;

namespace DAISY_Braille_Toolkit
{
    public partial class MainWindow : Window
    {
        public string SharePointProvisioningPath { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "Provisioning");

        private readonly AppSettingsStore _settingsStore = new();
        private AppSettings _settings = new();

        private List<VoiceInfo> _allVoices = new();
        private readonly MediaPlayer _player = new();
        private static readonly HttpClient _http = new();

        // TTS editor state
        private JobWorkspace? _job;
        private List<SegmentItem> _segments = new();
        private SegmentItem? _selectedSegment;

        public MainWindow()
        {
            InitializeComponent();

            _settings = _settingsStore.Load();
            InitModelCombo();
            LoadVoicesFromCache();
            InitOutputModeCombo();



            // Preview player wiring (gives feedback if audio fails)
            try
            {
                _player.Volume = 1.0;
                _player.IsMuted = false;

                _player.MediaOpened += (_, __) =>
                {
                    try
                    {
                        Dispatcher.Invoke(() => PreviewStatusText.Text = "Afspiller (preview)...");
                        _player.Position = TimeSpan.Zero;
                        _player.Play();
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => PreviewStatusText.Text = "Kunne ikke starte afspilning: " + ex.Message);
                        AppendLog("Preview play start fejl: " + ex);
                    }
                };

                _player.MediaEnded += (_, __) =>
                {
                    Dispatcher.Invoke(() => PreviewStatusText.Text = "Preview færdig." );
                };

                _player.MediaFailed += (_, e) =>
                {
                    var msg = e?.ErrorException?.Message ?? "Ukendt fejl";
                    Dispatcher.Invoke(() => PreviewStatusText.Text = "Kunne ikke afspille preview: " + msg);
                    AppendLog("Preview afspilning fejlede: " + e?.ErrorException);
                };
            }
            catch
            {
                // Ignore preview wiring errors
            }
            InitLanguageFilter();
            ApplySettingsToUi();
            RefreshVoiceDetails();

            RefreshApiKeyStatus();
        }

        // ---------- API KEY ----------
        private void SaveApiKey_Click(object sender, RoutedEventArgs e)
        {
            var key = ApiKeyBox?.Password?.Trim();

            if (string.IsNullOrWhiteSpace(key))
            {
                System.Windows.MessageBox.Show("Indtast en API key.");
                return;
            }

            SecretStore.SaveElevenLabsApiKey(key);
            if (ApiKeyBox != null) ApiKeyBox.Password = string.Empty;

            System.Windows.MessageBox.Show("API key er gemt lokalt (krypteret).");
            RefreshApiKeyStatus();
        }

        private void DeleteApiKey_Click(object sender, RoutedEventArgs e)
        {
            SecretStore.DeleteElevenLabsApiKey();
            if (ApiKeyBox != null) ApiKeyBox.Password = string.Empty;

            System.Windows.MessageBox.Show("Lokal API key er slettet.");
            RefreshApiKeyStatus();
        }

        private void RefreshApiKeyStatus()
        {
            if (ApiKeyStatusText == null) return;

            if (SecretStore.TryGetElevenLabsApiKeyStatus(out var source, out var tail, out var savedUtc))
            {
                var tailText = string.IsNullOrWhiteSpace(tail) ? "" : $" – slutter på …{tail}";

                if (source == SecretStore.ApiKeySource.Environment)
                {
                    ApiKeyStatusText.Text = $"API key sat via miljøvariabel (ELEVENLABS_API_KEY){tailText}";
                    return;
                }

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

        // ---------- SETTINGS ----------
        private void InitModelCombo()
        {
            ModelCombo.ItemsSource = ElevenLabsModels.All;
        }

        private void LoadVoicesFromCache()
        {
            try
            {
                var cache = new VoicesCacheStore();
                var data = cache.Load();
                if (data?.Voices is { Count: > 0 })
                {
                    _allVoices = data.Voices;
                    VoiceCombo.ItemsSource = _allVoices;
                    VoicesSyncText.Text = $"Cache: {data.SyncedUtc.ToLocalTime():dd-MM-yyyy HH:mm}";
                }
                else
                {
                    _allVoices = new List<VoiceInfo>();
                    VoiceCombo.ItemsSource = _allVoices;
                    VoicesSyncText.Text = "Ingen cache endnu";
                }
            }
            catch (Exception ex)
            {
                _allVoices = new List<VoiceInfo>();
                VoiceCombo.ItemsSource = _allVoices;
                VoicesSyncText.Text = "Kunne ikke læse cache";
                AppendLog("Fejl ved læsning af stemme-cache: " + ex.Message);
            }
        }

        private void InitLanguageFilter()
        {
            var langs = _allVoices
                .Select(v => v.LanguageForFilter)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            langs.Insert(0, "Alle");

            LanguageFilterCombo.ItemsSource = langs;
            LanguageFilterCombo.SelectedIndex = 0;
        }

        private void ApplySettingsToUi()
        {
            if (!string.IsNullOrWhiteSpace(_settings.DefaultModel))
                ModelCombo.SelectedItem = ElevenLabsModels.All.FirstOrDefault(m => m == _settings.DefaultModel) ?? ElevenLabsModels.All[0];
            else
                ModelCombo.SelectedItem = ElevenLabsModels.All[0];

            if (!string.IsNullOrWhiteSpace(_settings.DefaultVoiceId))
                VoiceCombo.SelectedValue = _settings.DefaultVoiceId;

            CleanupCheck.IsChecked = _settings.CleanupTempAfterJob;

            // SharePoint connection settings
            if (SharePointEnabledCheck != null)
                SharePointEnabledCheck.IsChecked = _settings.SharePointEnabled;
            if (SharePointSiteUrlBox != null)
                SharePointSiteUrlBox.Text = _settings.SharePointSiteUrl ?? "";
            if (SharePointTenantIdBox != null)
                SharePointTenantIdBox.Text = _settings.SharePointTenantId ?? "";
            if (SharePointClientIdBox != null)
                SharePointClientIdBox.Text = _settings.SharePointClientId ?? "";
            if (SharePointCountersListBox != null)
                SharePointCountersListBox.Text = string.IsNullOrWhiteSpace(_settings.SharePointCountersList) ? "DBT_Counters" : _settings.SharePointCountersList;
            if (SharePointProductionsListBox != null)
                SharePointProductionsListBox.Text = string.IsNullOrWhiteSpace(_settings.SharePointProductionsList) ? "DBT_Productions" : _settings.SharePointProductionsList;
            if (VolumeLabelPrefixBox != null)
                VolumeLabelPrefixBox.Text = _settings.VolumeLabelPrefix ?? "";
            if (SequenceDigitsBox != null)
                SequenceDigitsBox.Text = (_settings.SequenceDigits <= 0 ? 3 : _settings.SequenceDigits).ToString();

            SettingsSavedText.Text = "";
            PreviewStatusText.Text = "";
            SegmentsStatusText.Text = "";
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            _settings.DefaultModel = ModelCombo.SelectedItem as string ?? ElevenLabsModels.All[0];
            _settings.DefaultVoiceId = VoiceCombo.SelectedValue as string ?? "";
            _settings.CleanupTempAfterJob = CleanupCheck.IsChecked == true;

            // SharePoint connection settings
            _settings.SharePointEnabled = SharePointEnabledCheck.IsChecked == true;
            _settings.SharePointSiteUrl = SharePointSiteUrlBox.Text?.Trim() ?? "";
            _settings.SharePointTenantId = SharePointTenantIdBox.Text?.Trim() ?? "";
            _settings.SharePointClientId = SharePointClientIdBox.Text?.Trim() ?? "";
            _settings.SharePointCountersList = string.IsNullOrWhiteSpace(SharePointCountersListBox.Text) ? "DBT_Counters" : SharePointCountersListBox.Text.Trim();
            _settings.SharePointProductionsList = string.IsNullOrWhiteSpace(SharePointProductionsListBox.Text) ? "DBT_Productions" : SharePointProductionsListBox.Text.Trim();
            _settings.VolumeLabelPrefix = VolumeLabelPrefixBox.Text?.Trim() ?? "";

            if (int.TryParse(SequenceDigitsBox.Text?.Trim(), out var digits) && digits is >= 1 and <= 6)
                _settings.SequenceDigits = digits;
            else
                _settings.SequenceDigits = 3;

            _settingsStore.Save(_settings);
            SettingsSavedText.Text = $"Gemt {DateTime.Now:dd-MM-yyyy HH:mm}";
            AppendLog("Indstillinger gemt.");
        }

        private async void RefreshVoices_Click(object sender, RoutedEventArgs e)
        {
            SettingsSavedText.Text = "";
            VoicesSyncText.Text = "Henter...";

            if (!SecretStore.TryGetElevenLabsApiKey(out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
            {
                VoicesSyncText.Text = "Mangler API key";
                System.Windows.MessageBox.Show("Du skal gemme en ElevenLabs API key først, før vi kan hente stemmelisten.");
                return;
            }

            try
            {
                var api = new ElevenLabsApi(apiKey);
                var voices = await api.GetVoicesAsync();

                var cache = new VoicesCacheStore();
                cache.Save(new VoicesCache { SyncedUtc = DateTime.UtcNow, Voices = voices });

                _allVoices = voices;
                ApplyFilterAndBindVoices(preserveSelected: true);
                InitLanguageFilter();

                VoicesSyncText.Text = $"Synk: {DateTime.Now:dd-MM-yyyy HH:mm}";
                AppendLog($"Hentede {voices.Count} stemmer fra ElevenLabs.");
            }
            catch (Exception ex)
            {
                VoicesSyncText.Text = "Fejl ved hent";
                AppendLog("Fejl ved hentning af stemmer: " + ex);
                System.Windows.MessageBox.Show("Kunne ikke hente stemmeliste.\n\n" + ex.Message);
            }
        }

        private void LanguageFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilterAndBindVoices(preserveSelected: true);
        }

        private void ApplyFilterAndBindVoices(bool preserveSelected)
        {
            var selectedVoiceId = preserveSelected ? (VoiceCombo.SelectedValue as string) : null;

            var filter = LanguageFilterCombo.SelectedItem as string;
            IEnumerable<VoiceInfo> filtered = _allVoices;

            if (!string.IsNullOrWhiteSpace(filter) && !string.Equals(filter, "Alle", StringComparison.OrdinalIgnoreCase))
                filtered = filtered.Where(v => string.Equals(v.LanguageForFilter, filter, StringComparison.OrdinalIgnoreCase));

            var list = filtered.ToList();
            VoiceCombo.ItemsSource = list;

            if (!string.IsNullOrWhiteSpace(selectedVoiceId))
                VoiceCombo.SelectedValue = selectedVoiceId;

            RefreshVoiceDetails();
        }

        private void VoiceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshVoiceDetails();
        }

        private void RefreshVoiceDetails()
        {
            var v = VoiceCombo.SelectedItem as VoiceInfo;
            if (v == null)
            {
                VoiceDetailsText.Text = "";
                return;
            }

            var lang = string.IsNullOrWhiteSpace(v.Language) ? "Ukendt" : v.Language;
            var acc = string.IsNullOrWhiteSpace(v.Accent) ? "" : $" | Accent: {v.Accent}";
            var prev = v.HasPreview ? " | Preview: Ja" : " | Preview: Nej";
            VoiceDetailsText.Text = $"Sprog: {lang}{acc}{prev}";
        }

        // ---------- PREVIEW PLAYBACK ----------
        private async void PlayPreview_Click(object sender, RoutedEventArgs e)
        {
            PreviewStatusText.Text = "";

            var v = VoiceCombo.SelectedItem as VoiceInfo;
            if (v == null)
            {
                System.Windows.MessageBox.Show("Vælg en stemme først.");
                return;
            }

            if (!v.HasPreview)
            {
                System.Windows.MessageBox.Show("Denne stemme har ingen preview_url i API'et.");
                return;
            }

            try
            {
                PreviewStatusText.Text = "Henter preview...";
                AppendLog($"Afspil preview: {v.Name} ({v.VoiceId})");

                var file = await GetOrDownloadPreviewAsync(v.VoiceId, v.PreviewUrl);
                _player.Stop();
            _player.Close();
            _player.Open(new Uri(file, UriKind.Absolute));

            PreviewStatusText.Text = "Indlæser preview...";
            }
            catch (Exception ex)
            {
                PreviewStatusText.Text = "Fejl";
                AppendLog("Fejl ved preview-afspilning: " + ex);
                System.Windows.MessageBox.Show("Kunne ikke afspille preview.\n\n" + ex.Message);
            }
        }

        private void StopPreview_Click(object sender, RoutedEventArgs e)
        {
            _player.Stop();
            _player.Close();
            PreviewStatusText.Text = "Stoppet.";
        }

        private static async System.Threading.Tasks.Task<string> GetOrDownloadPreviewAsync(string voiceId, string previewUrl)
        {
            var outDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DAISY-Braille-Toolkit",
                "voice-previews");

            Directory.CreateDirectory(outDir);

            var file = Path.Combine(outDir, $"{voiceId}.mp3");

            if (File.Exists(file))
            {
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(file);
                if (age < TimeSpan.FromDays(30))
                    return file;
            }

            var bytes = await _http.GetByteArrayAsync(previewUrl);
            await File.WriteAllBytesAsync(file, bytes);
            return file;
        }

        // ---------- TTS EDITOR ----------
        private void BrowseJobFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WinForms.FolderBrowserDialog
            {
                Description = "Vælg en job-mappe (her gemmer vi segmenter: source/tts).",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            if (dlg.ShowDialog() == WinForms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
            {
                JobFolderBox.Text = dlg.SelectedPath;
                AppendLog("Job-mappe: " + dlg.SelectedPath);
            }
        }

        private void BrowseSourceFile_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WinForms.OpenFileDialog
            {
                Filter = "Text/Word (*.txt;*.docx)|*.txt;*.docx|Text files (*.txt)|*.txt|Word documents (*.docx)|*.docx|All files (*.*)|*.*",
                Title = "Vælg kildefil (txt/docx)"
            };

            if (dlg.ShowDialog() == WinForms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.FileName))
            {
                SourceFileBox.Text = dlg.FileName;
                AppendLog("Kilde: " + dlg.FileName);
            }
        }

        private void BuildSegments_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SegmentsStatusText.Text = "";
                _segments.Clear();
                _selectedSegment = null;
                SegmentsList.ItemsSource = null;
                SourceTextBox.Text = "";
                TtsTextBox.Text = "";

                var jobFolder = (JobFolderBox.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(jobFolder))
                {
                    System.Windows.MessageBox.Show("Vælg en job-mappe først.");
                    return;
                }

                var sourceFile = (SourceFileBox.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile))
                {
                    System.Windows.MessageBox.Show("Vælg en gyldig kilde-tekstfil først.");
                    return;
                }

                var modelId = ModelCombo.SelectedItem as string ?? ElevenLabsModels.Default;

            // IMPORTANT: Source may be .txt or .docx. For .docx we must extract
            // real text (and headings) instead of reading raw binary bytes.
            var sourceText = DocumentTextExtractor.ExtractText(sourceFile);

                _job = new JobWorkspace(jobFolder);
                _segments = _job.BuildSegmentsFromSourceText(sourceText, modelId);

                SegmentsList.ItemsSource = _segments;
                SegmentsStatusText.Text = $"Segmenter: {_segments.Count} (safe max: {TextSegmenter.GetSafeMaxChars(modelId)})";

                AppendLog($"Byggede {_segments.Count} segmenter i: {_job.SegmentsFolder}");
            }
            catch (Exception ex)
            {
                AppendLog("Fejl i BuildSegments: " + ex);
                System.Windows.MessageBox.Show("Fejl ved segmentering.\n\n" + ex.Message);
            }
        }

        private void SegmentsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SegmentsList.SelectedItem is not SegmentItem seg)
                return;

            _selectedSegment = seg;

            try
            {
                SourceTextBox.Text = File.Exists(seg.SourcePath) ? File.ReadAllText(seg.SourcePath, Encoding.UTF8) : "";
                TtsTextBox.Text = File.Exists(seg.TtsPath) ? File.ReadAllText(seg.TtsPath, Encoding.UTF8) : SourceTextBox.Text;
            }
            catch (Exception ex)
            {
                AppendLog("Fejl ved indlæsning af segment: " + ex.Message);
            }
        }

        private void CopySourceToTts_Selected_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSegment == null)
            {
                System.Windows.MessageBox.Show("Vælg et segment først.");
                return;
            }

            TtsTextBox.Text = SourceTextBox.Text;
            AppendLog($"Kopiér Source → TTS (segment {_selectedSegment.Index:0000})");
        }

        private void CopySourceToTts_All_Click(object sender, RoutedEventArgs e)
        {
            if (_segments.Count == 0)
            {
                System.Windows.MessageBox.Show("Byg segmenter først.");
                return;
            }

            foreach (var seg in _segments)
            {
                var src = File.Exists(seg.SourcePath) ? File.ReadAllText(seg.SourcePath, Encoding.UTF8) : "";
                File.WriteAllText(seg.TtsPath, src, Encoding.UTF8);
            }

            if (_selectedSegment != null && File.Exists(_selectedSegment.TtsPath))
                TtsTextBox.Text = File.ReadAllText(_selectedSegment.TtsPath, Encoding.UTF8);

            AppendLog("Kopiér Source → TTS (alle)");
        }

        private void SaveTts_Selected_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSegment == null)
            {
                System.Windows.MessageBox.Show("Vælg et segment først.");
                return;
            }

            File.WriteAllText(_selectedSegment.TtsPath, TtsTextBox.Text ?? "", Encoding.UTF8);
            AppendLog($"Gem TTS (segment {_selectedSegment.Index:0000})");
        }

        private void SaveTts_All_Click(object sender, RoutedEventArgs e)
        {
            if (_segments.Count == 0)
            {
                System.Windows.MessageBox.Show("Byg segmenter først.");
                return;
            }

            if (_selectedSegment != null)
                File.WriteAllText(_selectedSegment.TtsPath, TtsTextBox.Text ?? "", Encoding.UTF8);

            AppendLog("Gem TTS (alle): kun valgt segment gemmes fra UI i denne version.");
            System.Windows.MessageBox.Show(@"Gem TTS (alle):

I denne første version gemmes kun det valgte segment fra UI.
De andre gemmes når du redigerer dem og trykker 'Gem TTS (valgt)'.");
        }

        private void ApplyDanishFixes_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSegment == null)
            {
                System.Windows.MessageBox.Show("Vælg et segment først.");
                return;
            }

            TtsTextBox.Text = TtsTextRules.ApplyDanishFixes(TtsTextBox.Text ?? "");
            AppendLog($"Dansk-fixes (segment {_selectedSegment.Index:0000})");
        }

        private void InsertTag_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button b) return;
            var tag = (b.Content as string) ?? "";
            if (string.IsNullOrWhiteSpace(tag)) return;

            InsertAtCaret(TtsTextBox, tag + " ");
        }

        // Use fully qualified WPF TextBox to avoid ambiguity if WinForms is referenced elsewhere.
        private static void InsertAtCaret(System.Windows.Controls.TextBox box, string text)
        {
            var start = box.SelectionStart;
            box.Text = (box.Text ?? "").Insert(start, text);
            box.SelectionStart = start + text.Length;
            box.SelectionLength = 0;
            box.Focus();
        }

        private void AppendLog(string line)
        {
            LogText.AppendText(line + Environment.NewLine);
            LogText.ScrollToEnd();
        }
    }
}
