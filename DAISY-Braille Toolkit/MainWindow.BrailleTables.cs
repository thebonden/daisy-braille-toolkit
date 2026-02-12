using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using DAISY_Braille_Toolkit.Models;
using DAISY_Braille_Toolkit.Services;
using WinForms = System.Windows.Forms;

namespace DAISY_Braille_Toolkit
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<BrailleTableEntry> _brailleTables = new();

        private void InitBrailleTables()
        {
            if (BrailleTablesGrid == null) return;
            BrailleTablesGrid.ItemsSource = _brailleTables;
            LoadBrailleTablesFromSettings();
            RefreshBrailleTableCombo();
        }

        private void LoadBrailleTablesFromSettings()
        {
            _brailleTables.Clear();
            var items = _settings.BrailleTables ?? new List<BrailleTableEntry>();
            foreach (var item in items.OrderBy(t => t.TableId, StringComparer.OrdinalIgnoreCase))
            {
                _brailleTables.Add(new BrailleTableEntry
                {
                    TableId = item.TableId ?? string.Empty,
                    IsFavorite = item.IsFavorite
                });
            }
            RefreshBrailleTableCombo();
        }

        private void BrailleTables_Add_Click(object sender, RoutedEventArgs e)
        {
            _brailleTables.Add(new BrailleTableEntry());
            RefreshBrailleTableCombo();
        }

        private void BrailleTables_Remove_Click(object sender, RoutedEventArgs e)
        {
            if (BrailleTablesGrid?.SelectedItem is BrailleTableEntry entry)
            {
                _brailleTables.Remove(entry);
                RefreshBrailleTableCombo();
            }
        }

        private void BrailleTableCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (BrailleTableCombo?.SelectedValue is string id)
                _settings.SelectedBrailleTableId = id;
        }

        private void BrailleTables_Import_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var dlg = new WinForms.OpenFileDialog
                {
                    Filter = "JSON (*.json)|*.json|All files (*.*)|*.*",
                    Title = "Import braille tables",
                };

                if (dlg.ShowDialog() != WinForms.DialogResult.OK || string.IsNullOrWhiteSpace(dlg.FileName))
                    return;

                var json = File.ReadAllText(dlg.FileName);
                var tables = ParseBrailleTablesJson(json);
                _brailleTables.Clear();
                foreach (var t in tables)
                    _brailleTables.Add(t);
                RefreshBrailleTableCombo();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, LanguageManager.T("Title_Error", "Error"));
            }
        }

        private void BrailleTables_Export_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var dlg = new WinForms.SaveFileDialog
                {
                    Filter = "JSON (*.json)|*.json|All files (*.*)|*.*",
                    Title = "Export braille tables",
                    FileName = "braille-tables.json"
                };

                if (dlg.ShowDialog() != WinForms.DialogResult.OK || string.IsNullOrWhiteSpace(dlg.FileName))
                    return;

                var data = _brailleTables
                    .Where(t => !string.IsNullOrWhiteSpace(t.TableId))
                    .Select(t => new BrailleTableEntry { TableId = t.TableId.Trim(), IsFavorite = t.IsFavorite })
                    .ToList();

                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(dlg.FileName, json);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, LanguageManager.T("Title_Error", "Error"));
            }
        }

        private async void BrailleTables_Fetch_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cmd = ResolvePipelineCmd();
                var result = await RunProcessAsync(cmd, "list-braille-tables");

                if (result.ExitCode != 0)
                {
                    throw new InvalidOperationException($"DP2 error ({result.ExitCode}): {result.ErrorText.Trim()}");
                }

                var parsed = ParseBrailleTablesFromOutput(result.OutputText);
                if (parsed.Count == 0)
                {
                    System.Windows.MessageBox.Show("No braille tables found in DP2 output.", LanguageManager.T("Title_Info", "Info"));
                    return;
                }

                _brailleTables.Clear();
                foreach (var item in parsed)
                    _brailleTables.Add(item);
                RefreshBrailleTableCombo();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, LanguageManager.T("Title_Error", "Error"));
            }
        }

        private void RefreshBrailleTableCombo()
        {
            if (BrailleTableCombo == null) return;

            var list = _brailleTables
                .Where(t => !string.IsNullOrWhiteSpace(t.TableId))
                .OrderByDescending(t => t.IsFavorite)
                .ThenBy(t => t.TableId, StringComparer.OrdinalIgnoreCase)
                .Select(t => new BrailleTableEntry { TableId = t.TableId.Trim(), IsFavorite = t.IsFavorite })
                .ToList();

            var selected = string.IsNullOrWhiteSpace(_settings.SelectedBrailleTableId)
                ? null
                : _settings.SelectedBrailleTableId;

            BrailleTableCombo.ItemsSource = list;

            if (!string.IsNullOrWhiteSpace(selected) && list.Any(t => string.Equals(t.TableId, selected, StringComparison.OrdinalIgnoreCase)))
            {
                BrailleTableCombo.SelectedValue = selected;
            }
            else if (list.Count > 0)
            {
                BrailleTableCombo.SelectedIndex = 0;
            }
            else
            {
                BrailleTableCombo.SelectedIndex = -1;
            }
        }

        private static List<BrailleTableEntry> ParseBrailleTablesJson(string json)
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Expected a JSON array of tables.");

            var results = new List<BrailleTableEntry>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var tableId = el.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(tableId))
                        results.Add(new BrailleTableEntry { TableId = tableId.Trim() });
                    continue;
                }

                if (el.ValueKind == JsonValueKind.Object)
                {
                    var tableId = el.TryGetProperty("tableId", out var tableProp) ? tableProp.GetString() : null;
                    var isFav = el.TryGetProperty("isFavorite", out var favProp) && favProp.GetBoolean();
                    if (!string.IsNullOrWhiteSpace(tableId))
                        results.Add(new BrailleTableEntry { TableId = tableId.Trim(), IsFavorite = isFav });
                }
            }

            return results;
        }

        private static List<BrailleTableEntry> ParseBrailleTablesFromOutput(string output)
        {
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".ctb", ".utb", ".cti", ".uti", ".dis", ".tbl", ".dic"
            };

            var lines = (output ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();

            var results = new List<BrailleTableEntry>();
            foreach (var line in lines)
            {
                var name = line.Trim();
                var ext = Path.GetExtension(name);
                if (!string.IsNullOrWhiteSpace(ext) && extensions.Contains(ext))
                    results.Add(new BrailleTableEntry { TableId = name });
            }

            return results;
        }

        private static string ResolvePipelineCmd()
        {
            var local = Path.Combine(AppContext.BaseDirectory, "dp2.exe");
            if (File.Exists(local))
                return local;
            return "pipeline2";
        }

        private static async Task<(int ExitCode, string OutputText, string ErrorText)> RunProcessAsync(string fileName, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start process.");
            var output = await proc.StandardOutput.ReadToEndAsync();
            var error = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return (proc.ExitCode, output, error);
        }

        private void SyncBrailleTablesToSettings()
        {
            if (BrailleTablesGrid == null) return;
            BrailleTablesGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
            BrailleTablesGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);

            _settings.BrailleTables = _brailleTables
                .Where(t => !string.IsNullOrWhiteSpace(t.TableId))
                .Select(t => new BrailleTableEntry
                {
                    TableId = t.TableId.Trim(),
                    IsFavorite = t.IsFavorite
                })
                .GroupBy(t => t.TableId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }
    }
}
