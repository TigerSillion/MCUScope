using MCUScope.ViewModels;
using Microsoft.Win32;
using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MCUScope.Dialogs
{
    public partial class TimetablePlayerDialog : Window
    {
        private readonly MainViewModel _vm;
        private DataTable _csvData = new();
        private CancellationTokenSource? _cts;
        private bool _isPaused;
        private string[] _variableNames = Array.Empty<string>();
        private double[] _times = Array.Empty<double>();
        private double[][] _values = Array.Empty<double[]>();
        private int _currentRow;

        public TimetablePlayerDialog(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
        }

        private void OnOpenFile(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                Title = "Open Timetable CSV"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                LoadCsvFile(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load CSV: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadCsvFile(string filePath)
        {
            var lines = File.ReadAllLines(filePath);
            if (lines.Length < 2) return;

            // Parse header
            var headers = lines[0].Split(',').Select(h => h.Trim()).ToArray();
            _variableNames = headers.Skip(1).ToArray(); // Skip time column

            _csvData = new DataTable();
            foreach (var h in headers)
                _csvData.Columns.Add(h, typeof(string));

            // Parse data
            _times = new double[lines.Length - 1];
            _values = new double[_variableNames.Length][];
            for (int v = 0; v < _variableNames.Length; v++)
                _values[v] = new double[lines.Length - 1];

            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                var row = _csvData.NewRow();
                for (int j = 0; j < Math.Min(parts.Length, headers.Length); j++)
                    row[j] = parts[j].Trim();
                _csvData.Rows.Add(row);

                if (parts.Length > 0 && double.TryParse(parts[0].Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var time))
                {
                    _times[i - 1] = time;
                }

                for (int v = 0; v < _variableNames.Length && v + 1 < parts.Length; v++)
                {
                    if (double.TryParse(parts[v + 1].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var val))
                    {
                        _values[v][i - 1] = val;
                    }
                }
            }

            DataPreview.ItemsSource = _csvData.DefaultView;
            CurrentRowText.Text = $"0/{_times.Length}";
        }

        private async void OnPlay(object sender, RoutedEventArgs e)
        {
            if (_times.Length == 0) return;
            if (_isPaused)
            {
                _isPaused = false;
                StatusText.Text = "Playing";
                return;
            }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _currentRow = 0;
            StatusText.Text = "Playing";

            try
            {
                await PlayAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Normal stop
            }
        }

        private async Task PlayAsync(CancellationToken token)
        {
            double startTime = _times[0];

            for (_currentRow = 0; _currentRow < _times.Length; _currentRow++)
            {
                token.ThrowIfCancellationRequested();

                while (_isPaused)
                {
                    await Task.Delay(100, token);
                }

                // Wait until the right time
                if (_currentRow > 0)
                {
                    double delay = (_times[_currentRow] - _times[_currentRow - 1]);
                    if (delay > 0)
                    {
                        int delayMs = (int)(delay * 1000);
                        await Task.Delay(Math.Max(delayMs, 1), token);
                    }
                }

                // Send values
                Dispatcher.Invoke(() =>
                {
                    CurrentTimeText.Text = $"{_times[_currentRow]:F3}s";
                    CurrentRowText.Text = $"{_currentRow + 1}/{_times.Length}";

                    // Highlight current row in grid
                    if (_currentRow < DataPreview.Items.Count)
                        DataPreview.SelectedIndex = _currentRow;
                });

                // Write variables to target
                for (int v = 0; v < _variableNames.Length; v++)
                {
                    // In real implementation, write via ICS protocol
                    // _icsService.RequestWriteVariable(_variableNames[v], _values[v][_currentRow]);
                }
            }

            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "Completed";
            });
        }

        private void OnStop(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _isPaused = false;
            _currentRow = 0;
            StatusText.Text = "Stopped";
            CurrentTimeText.Text = "0.000s";
            CurrentRowText.Text = $"0/{_times.Length}";
        }

        private void OnPause(object sender, RoutedEventArgs e)
        {
            _isPaused = !_isPaused;
            StatusText.Text = _isPaused ? "Paused" : "Playing";
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Cancel();
            base.OnClosed(e);
        }
    }
}
