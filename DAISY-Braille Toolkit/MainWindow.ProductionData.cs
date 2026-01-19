using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using DAISY_Braille_Toolkit.Models;
using Microsoft.Win32;

namespace DAISY_Braille_Toolkit
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<ProductionDataRow> _productionRows = new();
        private bool _productionDataInitialized;

        private void ProductionTab_Loaded(object sender, RoutedEventArgs e)
        {
            if (_productionDataInitialized)
                return;

            _productionDataInitialized = true;

            ProductionGrid.ItemsSource = _productionRows;

            if (_productionRows.Count == 0)
            {
                _productionRows.Add(new ProductionDataRow());
            }

            // Suggest a default CSV path inside the job folder (if available)
            if (string.IsNullOrWhiteSpace(ProductionCsvPathBox.Text))
            {
                var jobFolder = SafeText(JobFolderBox?.Text);
                if (!string.IsNullOrWhiteSpace(jobFolder) && Directory.Exists(jobFolder))
                {
                    ProductionCsvPathBox.Text = Path.Combine(jobFolder, "production-data.csv");
                }
            }
        }

        private void Production_BrowseCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = string.IsNullOrWhiteSpace(ProductionCsvPathBox.Text)
                    ? "production-data.csv"
                    : Path.GetFileName(ProductionCsvPathBox.Text)
            };

            try
            {
                var current = ProductionCsvPathBox.Text;
                if (!string.IsNullOrWhiteSpace(current))
                {
                    var dir = Path.GetDirectoryName(current);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                        dlg.InitialDirectory = dir;
                }
            }
            catch
            {
                // ignore
            }

            if (dlg.ShowDialog(this) == true)
            {
                ProductionCsvPathBox.Text = dlg.FileName;
            }
        }

        private void Production_AddCurrentJob_Click(object sender, RoutedEventArgs e)
        {
            var row = CreateRowFromCurrentUi();
            _productionRows.Add(row);
            ProductionGrid.SelectedItem = row;
            ProductionGrid.ScrollIntoView(row);
        }

        private void Production_RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            if (ProductionGrid.SelectedItem is ProductionDataRow row)
            {
                _productionRows.Remove(row);
            }
        }

        private void Production_CopySelected_Click(object sender, RoutedEventArgs e)
        {
            if (ProductionGrid.SelectedItem is ProductionDataRow row)
            {
                System.Windows.Clipboard.SetText(row.ToCsvRow());
            }
        }

        private void Production_OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var path = ProductionCsvPathBox.Text;
            if (string.IsNullOrWhiteSpace(path))
                return;

            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return;

            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            }
            catch
            {
                // ignore
            }
        }

        private void Production_ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var path = SafeText(ProductionCsvPathBox.Text);
            if (string.IsNullOrWhiteSpace(path))
            {
                Production_BrowseCsv_Click(sender, e);
                path = SafeText(ProductionCsvPathBox.Text);
                if (string.IsNullOrWhiteSpace(path))
                    return;
            }

            try
            {
                ExportCsv(path);
                System.Windows.MessageBox.Show(this, "CSV exported.", "Production data", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportCsv(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var writeHeader = !File.Exists(path) || new FileInfo(path).Length == 0;

            using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var sw = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            if (writeHeader)
            {
                sw.WriteLine(ProductionDataRow.CsvHeader);
            }

            foreach (var row in _productionRows)
            {
                if (row == null)
                    continue;
                sw.WriteLine(row.ToCsvRow());
            }
        }

        private ProductionDataRow CreateRowFromCurrentUi()
        {
            var row = new ProductionDataRow
            {
                Timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            // Paths from the Job tab
            row.JobFolder = SafeText(JobFolderBox?.Text);
            row.SourceFile = SafeText(SourceFileBox?.Text);

            // Model selection
            row.ModelId = ModelCombo?.SelectedItem?.ToString() ?? string.Empty;

            // Voice selection
            if (VoiceCombo?.SelectedItem is VoiceInfo vi)
            {
                row.VoiceId = vi.VoiceId ?? string.Empty;
                row.VoiceName = !string.IsNullOrWhiteSpace(vi.Name) ? vi.Name : (vi.DisplayName ?? string.Empty);
                row.Language = vi.Language ?? string.Empty;
            }
            else
            {
                row.VoiceName = VoiceCombo?.SelectedItem?.ToString() ?? string.Empty;
            }

            return row;
        }

        private static string SafeText(string? text) => string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
    }
}
