using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using DAISY_Braille_Toolkit.Services;

namespace DAISY_Braille_Toolkit
{
    public partial class MainWindow : Window
    {
        // ---- Navigation (menu-driven) ----
        private void Go_RunJob_Click(object sender, RoutedEventArgs e) => SelectMainView(0);
        private void Go_Tts_Click(object sender, RoutedEventArgs e) => SelectMainView(1);
        private void Go_Voices_Click(object sender, RoutedEventArgs e) => SelectMainView(2);
        private void Go_Settings_Click(object sender, RoutedEventArgs e) => SelectMainView(3);
        private void Go_ProductionData_Click(object sender, RoutedEventArgs e) => SelectMainView(4);
        private void Go_Log_Click(object sender, RoutedEventArgs e) => SelectMainView(5);

        private void SelectMainView(int index)
        {
            try
            {
                if (MainTabs == null) return;
                if (index < 0 || index >= MainTabs.Items.Count) return;
                MainTabs.SelectedIndex = index;
            }
            catch { }
        }

        // Optional keyboard shortcuts (matches the hints in the View menu)
        private void RootWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
                return;

            // Avoid capturing Ctrl+V/C/X inside editors? We only handle a small set.
            switch (e.Key)
            {
                case Key.D1:
                case Key.NumPad1:
                    SelectMainView(0);
                    e.Handled = true;
                    break;
                case Key.D2:
                case Key.NumPad2:
                    SelectMainView(1);
                    e.Handled = true;
                    break;
                case Key.D3:
                case Key.NumPad3:
                    SelectMainView(2);
                    e.Handled = true;
                    break;
                case Key.D4:
                case Key.NumPad4:
                    SelectMainView(3);
                    e.Handled = true;
                    break;
                case Key.D5:
                case Key.NumPad5:
                    SelectMainView(4);
                    e.Handled = true;
                    break;
                case Key.L:
                    SelectMainView(5);
                    e.Handled = true;
                    break;
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var title = LanguageManager.T("About_Title", "About");
            var text = LanguageManager.T("About_Text", "DAISY-Braille Toolkit");

            System.Windows.MessageBox.Show(
                text,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void OpenJobFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folder = (JobFolderBox?.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    System.Windows.MessageBox.Show(
                        LanguageManager.T("Msg_SelectValidJobFolder", "Select a valid job folder first."),
                        LanguageManager.T("Title_Info", "Info"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    LanguageManager.T("Msg_CouldNotOpenFolder", "Could not open the folder.") + "\n\n" + ex.Message,
                    LanguageManager.T("Title_Error", "Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OpenSharePointSetupFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folder = SharePointProvisioningPath;
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    System.Windows.MessageBox.Show(
                        "SharePoint provisioning folder not found:\n" + folder,
                        LanguageManager.T("Title_Error", "Error"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    LanguageManager.T("Msg_CouldNotOpenFolder", "Could not open the folder.") + "\n\n" + ex.Message,
                    LanguageManager.T("Title_Error", "Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }


        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogText?.Clear();
            }
            catch { }
        }

        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var text = LogText?.Text ?? "";
                if (string.IsNullOrWhiteSpace(text))
                {
                    System.Windows.MessageBox.Show(
                        LanguageManager.T("Msg_LogEmpty", "The log is empty."),
                        LanguageManager.T("Title_Info", "Info"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                System.Windows.Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    LanguageManager.T("Msg_CouldNotCopyLog", "Could not copy the log.") + "\n\n" + ex.Message,
                    LanguageManager.T("Title_Error", "Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
