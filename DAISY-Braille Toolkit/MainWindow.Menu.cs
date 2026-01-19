using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using DAISY_Braille_Toolkit.Services;

namespace DAISY_Braille_Toolkit
{
    public partial class MainWindow : Window
    {
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
