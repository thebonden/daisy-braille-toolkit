using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DAISY_Braille_Toolkit.Models;
using DAISY_Braille_Toolkit.Services;
using WinForms = System.Windows.Forms;

namespace DAISY_Braille_Toolkit
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<ProductionDataRow> _productionRows = new();
        private IReadOnlyList<string> _productionColumns = Array.Empty<string>();
        private bool _productionInitialized;

        private void ProductionTab_Loaded(object sender, RoutedEventArgs e)
        {
            if (_productionInitialized) return;
            _productionInitialized = true;

            // 1) Load schema (single source of truth)
            _productionColumns = ProductionSchemaLoader.LoadColumns();

            // 2) Build columns dynamically
            BuildProductionGridColumns();

            // 3) Bind rows
            ProductionGrid.ItemsSource = _productionRows;
            if (_productionRows.Count == 0)
                _productionRows.Add(new ProductionDataRow());

            // 4) Default CSV path from settings
            if (string.IsNullOrWhiteSpace(ProductionCsvPathBox.Text) && !string.IsNullOrWhiteSpace(_settings.ProductionCsvPath))
                ProductionCsvPathBox.Text = _settings.ProductionCsvPath;
        }

        private void BuildProductionGridColumns()
        {
            try
            {
                ProductionGrid.Columns.Clear();

                foreach (var col in _productionColumns)
                {
                    var binding = new System.Windows.Data.Binding($"[{col}]")
                    {
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                    };

                    var column = new DataGridTextColumn
                    {
                        Header = col,
                        Binding = binding,
                        Width = col.Equals("Titel", StringComparison.OrdinalIgnoreCase)
                            ? new DataGridLength(1, DataGridLengthUnitType.Star)
                            : DataGridLength.Auto
                    };

                    ProductionGrid.Columns.Add(column);
                }
            }
            catch (Exception ex)
            {
                AppendLog("Production grid columns error: " + ex);
            }
        }

        private void Production_BrowseCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var dlg = new WinForms.SaveFileDialog
                {
                    Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*",
                    Title = "Choose CSV file",
                    FileName = string.IsNullOrWhiteSpace(ProductionCsvPathBox.Text) ? "productions.csv" : Path.GetFileName(ProductionCsvPathBox.Text)
                };

                if (dlg.ShowDialog() == WinForms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.FileName))
                {
                    ProductionCsvPathBox.Text = dlg.FileName;
                    _settings.ProductionCsvPath = dlg.FileName;
                    _settingsStore.Save(_settings);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, LanguageManager.T("Title_Error", "Error"));
            }
        }

        private void Production_ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = (ProductionCsvPathBox.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(path))
                {
                    System.Windows.MessageBox.Show("Select a CSV file first.", LanguageManager.T("Title_Info", "Info"));
                    return;
                }

                // Persist path
                _settings.ProductionCsvPath = path;
                _settingsStore.Save(_settings);

                // Ensure current edits are committed
                ProductionGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                ProductionGrid.CommitEdit(DataGridEditingUnit.Row, true);

                var rows = _productionRows
                    .Where(r => RowHasAnyData(r))
                    .ToList();

                if (rows.Count == 0)
                {
                    System.Windows.MessageBox.Show("No rows to export.", LanguageManager.T("Title_Info", "Info"));
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var writeHeader = !File.Exists(path) || new FileInfo(path).Length == 0;

                using var sw = new StreamWriter(path, append: true, encoding: new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                if (writeHeader)
                    sw.WriteLine(ProductionDataRow.CsvHeader(_productionColumns));

                foreach (var r in rows)
                    sw.WriteLine(r.ToCsvRow(_productionColumns));

                AppendLog($"Exported {rows.Count} production row(s) to CSV: {path}");
                System.Windows.MessageBox.Show($"Exported {rows.Count} row(s).", LanguageManager.T("Title_Info", "Info"));
            }
            catch (Exception ex)
            {
                AppendLog("Export CSV error: " + ex);
                System.Windows.MessageBox.Show(ex.Message, LanguageManager.T("Title_Error", "Error"));
            }
        }

        private static bool RowHasAnyData(ProductionDataRow row)
        {
            try
            {
                // A row counts as "data" if at least one field is non-empty.
                return row.Fields.Values.Any(v => !string.IsNullOrWhiteSpace(v));
            }
            catch
            {
                return false;
            }
        }

        private void Production_AddCurrentJob_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var row = CreateRowFromCurrentUi();
                _productionRows.Add(row);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, LanguageManager.T("Title_Error", "Error"));
            }
        }

        private void Production_RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            if (ProductionGrid.SelectedItem is ProductionDataRow row)
                _productionRows.Remove(row);
        }

        private void Production_CopySelected_Click(object sender, RoutedEventArgs e)
        {
            if (ProductionGrid.SelectedItem is not ProductionDataRow row)
                return;

            try
            {
                System.Windows.Clipboard.SetText(row.ToCsvRow(_productionColumns));
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, LanguageManager.T("Title_Error", "Error"));
            }
        }

        private void Production_OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = (ProductionCsvPathBox.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(path)) return;

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true
                    });
                }
            }
            catch { }
        }

        private ProductionDataRow CreateRowFromCurrentUi()
        {
            var row = new ProductionDataRow();

            // Helpful defaults from current UI
            var now = DateTime.Now;
            var sourcePath = (SourceFileBox.Text ?? "").Trim();

            var modelId = (ModelCombo.SelectedItem as string) ?? _settings.ElevenLabsModelId;
            var voiceId = (VoiceCombo.SelectedValue as string) ?? _settings.ElevenLabsVoiceId;

            var voiceName = "";
            var voiceLang = "";

            var vi = _allVoices.FirstOrDefault(v => string.Equals(v.VoiceId, voiceId, StringComparison.OrdinalIgnoreCase));
            if (VoiceCombo.SelectedItem is VoiceInfo selected)
                vi = selected;

            if (vi != null)
            {
                voiceName = vi.Name;
                voiceLang = vi.LanguageForFilter;
            }

            SetIfFound(row, FindCol("Titel"), !string.IsNullOrWhiteSpace(sourcePath) ? Path.GetFileNameWithoutExtension(sourcePath) : "");
            SetIfFound(row, FindCol("Stemme"), voiceName);
            SetIfFound(row, FindCol("Sprog"), voiceLang);
            SetIfFound(row, FindCol("Dato for Intale"), now.ToString("yyyy-MM-dd"));
            SetIfFound(row, FindCol("Tid for intale"), now.ToString("HH:mm"));
            SetIfFound(row, FindCol("Data"), now.ToString("yyyy-MM-dd"));

            SetIfFound(row, FindCol("orginaldokomenter"), !string.IsNullOrWhiteSpace(sourcePath) ? Path.GetFileName(sourcePath) : "");

            // Keep these around if the schema still includes older English fields
            SetIfFound(row, FindCol("Model"), modelId);
            SetIfFound(row, FindCol("VoiceId"), voiceId);
            SetIfFound(row, FindCol("Source"), sourcePath);
            SetIfFound(row, FindCol("Job"), (JobFolderBox.Text ?? "").Trim());

            return row;
        }

        private string? FindCol(params string[] tokens)
        {
            foreach (var c in _productionColumns)
            {
                var ok = true;
                foreach (var t in tokens)
                {
                    if (!c.Contains(t, StringComparison.OrdinalIgnoreCase))
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) return c;
            }
            return null;
        }

        private static void SetIfFound(ProductionDataRow row, string? col, string value)
        {
            if (string.IsNullOrWhiteSpace(col)) return;
            row[col] = value ?? string.Empty;
        }
    }
}
