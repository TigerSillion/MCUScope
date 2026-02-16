using MCUScope.Dialogs;
using MCUScope.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace MCUScope.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Closing += (s, e) =>
            {
                if (DataContext is MainViewModel vm)
                    vm.Dispose();
            };
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();

        private void OnVariableSettings(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                var dlg = new VariableSettingsDialog(vm) { Owner = this };
                dlg.ShowDialog();
            }
        }

        private void OnCommunicationSettings(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                var dlg = new CommunicationSettingsDialog(vm) { Owner = this };
                dlg.ShowDialog();
            }
        }

        private void OnArrayEditor(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                var dlg = new ArrayEditorDialog(vm) { Owner = this };
                dlg.Show();
            }
        }

        private void OnCustomControlPanel(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                var dlg = new CustomControlPanelDialog(vm) { Owner = this };
                dlg.Show();
            }
        }

        private void OnTimetablePlayer(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                var dlg = new TimetablePlayerDialog(vm) { Owner = this };
                dlg.Show();
            }
        }

        private void OnAbout(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "MCUScope Version 1.0.0.0\n\n" +
                "Real time variable waveform viewer for MCU.\n" +
                "Compatible with ICS++ protocol.\n\n" +
                "Open Source Components / Libraries:\n" +
                "OxyPlot (MIT)\n" +
                "MathNet.Numerics (MIT)\n" +
                "CommunityToolkit.Mvvm (MIT)\n" +
                "System.IO.Ports (MIT)",
                "About MCUScope",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void OnChineseManual(object sender, RoutedEventArgs e)
        {
            string[] candidatePaths =
            {
                Path.Combine(AppContext.BaseDirectory, "docs", "GUI_OPERATION_MANUAL_CN.md"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "GUI_OPERATION_MANUAL_CN.md"))
            };

            string manualPath = string.Empty;
            foreach (var path in candidatePaths)
            {
                if (File.Exists(path))
                {
                    manualPath = path;
                    break;
                }
            }

            if (string.IsNullOrEmpty(manualPath))
            {
                MessageBox.Show("Manual file not found: docs/GUI_OPERATION_MANUAL_CN.md",
                    "Manual Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = manualPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open manual: {ex.Message}",
                    "Open Manual Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
