using MCUScope.Models;
using MCUScope.Services;
using MCUScope.ViewModels;
using Microsoft.Win32;
using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;

namespace MCUScope.Dialogs
{
    public partial class ArrayEditorDialog : Window
    {
        private readonly MainViewModel _vm;
        private DataTable _dataTable = new();

        public ArrayEditorDialog(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;

            foreach (var name in SessionState.Instance.VariableNames)
                OriginCombo.Items.Add(name);

            InitializeDataTable();
        }

        private void InitializeDataTable()
        {
            int columns = int.TryParse(ColumnsBox.Text, out var c) ? Math.Max(1, Math.Min(c, 32)) : 8;
            int count = int.TryParse(CountBox.Text, out var cnt) ? Math.Max(1, Math.Min(cnt, 1024)) : 64;
            int start = int.TryParse(StartBox.Text, out var s) ? s : 0;

            _dataTable = new DataTable();
            _dataTable.Columns.Add("Index", typeof(string));
            for (int col = 0; col < columns; col++)
                _dataTable.Columns.Add($"[{col}]", typeof(string));

            int index = start;
            int rows = (count + columns - 1) / columns;
            for (int row = 0; row < rows; row++)
            {
                var dataRow = _dataTable.NewRow();
                dataRow["Index"] = index.ToString();
                for (int col = 0; col < columns && index < start + count; col++, index++)
                {
                    dataRow[$"[{col}]"] = "0";
                }
                _dataTable.Rows.Add(dataRow);
            }

            ArrayDataGrid.ItemsSource = _dataTable.DefaultView;
        }

        private (uint baseAddress, VariableType varType, int elementSize)? ResolveArrayBase()
        {
            var session = SessionState.Instance;
            var type = (VariableType)(TypeCombo.SelectedIndex >= 0 ? TypeCombo.SelectedIndex : 5);
            int elementSize = type switch
            {
                VariableType.UInt8 or VariableType.Int8 or VariableType.Bool or VariableType.Logic => 1,
                VariableType.UInt16 or VariableType.Int16 => 2,
                _ => 4
            };

            uint baseAddress = 0;
            string origin = OriginCombo.Text?.Trim() ?? "";

            // Try resolve as variable name first
            if (session.IcsService.TryResolveVariable(origin, out var variable))
            {
                baseAddress = variable.Address;
            }
            else if (origin.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (!uint.TryParse(origin.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out baseAddress))
                    return null;
            }
            else if (!uint.TryParse(origin, NumberStyles.Integer, CultureInfo.InvariantCulture, out baseAddress))
            {
                return null;
            }

            // Apply offset
            if (OffsetBox.Text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (uint.TryParse(OffsetBox.Text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var offset))
                    baseAddress += offset;
            }
            else if (uint.TryParse(OffsetBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offsetDec))
            {
                baseAddress += offsetDec;
            }

            return (baseAddress, type, elementSize);
        }

        private async void OnRead(object sender, RoutedEventArgs e)
        {
            var resolved = ResolveArrayBase();
            if (!resolved.HasValue)
            {
                MessageBox.Show("Invalid origin address or variable name.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var (baseAddress, varType, elementSize) = resolved.Value;
            int columns = int.TryParse(ColumnsBox.Text, out var c) ? Math.Max(1, Math.Min(c, 32)) : 8;
            int count = int.TryParse(CountBox.Text, out var cnt) ? Math.Max(1, Math.Min(cnt, 1024)) : 64;
            int start = int.TryParse(StartBox.Text, out var s) ? s : 0;
            double scale = double.TryParse(ScaleBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var sc) ? sc : 1.0;

            var session = SessionState.Instance;
            var ics = session.IcsService;

            // Read each element by sending individual read requests
            InitializeDataTable();

            // Send read requests for each element address
            for (int i = 0; i < count; i++)
            {
                uint addr = (uint)(baseAddress + (start + i) * elementSize);
                string tempName = $"_arr_{addr:X8}";

                // Create temporary variable for this element
                var tempVar = new VariableInfo
                {
                    Name = tempName,
                    Address = addr,
                    OriginalType = varType,
                    ModifiedType = varType,
                    Scale = scale,
                    ReadEnabled = true
                };

                // Add temporarily if not exists, then send read
                if (!ics.Variables.Any(v => v.Address == addr))
                {
                    ics.Variables.Add(tempVar);
                }
                ics.RequestReadVariable(tempName);
            }

            MessageBox.Show($"Sent {count} read requests starting at 0x{baseAddress:X8}.\nValues will populate as responses arrive.",
                "Array Read", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnWrite(object sender, RoutedEventArgs e)
        {
            var resolved = ResolveArrayBase();
            if (!resolved.HasValue)
            {
                MessageBox.Show("Invalid origin address or variable name.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var (baseAddress, varType, elementSize) = resolved.Value;
            int columns = int.TryParse(ColumnsBox.Text, out var c) ? Math.Max(1, Math.Min(c, 32)) : 8;
            int start = int.TryParse(StartBox.Text, out var s) ? s : 0;
            double scale = double.TryParse(ScaleBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var sc) ? sc : 1.0;

            var session = SessionState.Instance;
            var ics = session.IcsService;
            int writeCount = 0;

            int index = start;
            foreach (DataRow row in _dataTable.Rows)
            {
                for (int col = 0; col < columns; col++)
                {
                    string colName = $"[{col}]";
                    if (!_dataTable.Columns.Contains(colName)) break;

                    string? cellValue = row[colName]?.ToString();
                    if (string.IsNullOrEmpty(cellValue)) continue;

                    if (double.TryParse(cellValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    {
                        uint addr = (uint)(baseAddress + index * elementSize);
                        string tempName = $"_arr_{addr:X8}";

                        var tempVar = new VariableInfo
                        {
                            Name = tempName,
                            Address = addr,
                            OriginalType = varType,
                            ModifiedType = varType,
                            Scale = scale,
                            WriteEnabled = true
                        };

                        if (!ics.Variables.Any(v => v.Address == addr))
                            ics.Variables.Add(tempVar);

                        ics.RequestWriteVariable(tempName, value);
                        writeCount++;
                    }
                    index++;
                }
            }

            LogService.Info($"ArrayEditor: Wrote {writeCount} values starting at 0x{baseAddress:X8}");
            MessageBox.Show($"Sent {writeCount} write commands.", "Array Write",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnExportCsv(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                Title = "Export Array Data"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    using var writer = new StreamWriter(dlg.FileName);
                    var headers = _dataTable.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
                    writer.WriteLine(string.Join(",", headers));
                    foreach (DataRow row in _dataTable.Rows)
                    {
                        var values = row.ItemArray.Select(v => v?.ToString() ?? "");
                        writer.WriteLine(string.Join(",", values));
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OnImportCsv(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                Title = "Import Array Data"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var lines = File.ReadAllLines(dlg.FileName);
                    if (lines.Length < 2) return;

                    int columns = int.TryParse(ColumnsBox.Text, out var c) ? Math.Max(1, c) : 8;
                    _dataTable = new DataTable();
                    _dataTable.Columns.Add("Index", typeof(string));
                    for (int col = 0; col < columns; col++)
                        _dataTable.Columns.Add($"[{col}]", typeof(string));

                    for (int i = 1; i < lines.Length; i++)
                    {
                        var parts = lines[i].Split(',');
                        var row = _dataTable.NewRow();
                        for (int j = 0; j < Math.Min(parts.Length, _dataTable.Columns.Count); j++)
                            row[j] = parts[j].Trim();
                        _dataTable.Rows.Add(row);
                    }

                    ArrayDataGrid.ItemsSource = _dataTable.DefaultView;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Import failed: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
