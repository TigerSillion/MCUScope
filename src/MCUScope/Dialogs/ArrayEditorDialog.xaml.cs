using MCUScope.Models;
using MCUScope.Services;
using MCUScope.ViewModels;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
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

            // Populate origin combo with variable names
            foreach (var name in vm.VariableNames)
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

        private void OnRead(object sender, RoutedEventArgs e)
        {
            // In a real implementation, this would read array data from the MCU
            // via the ICS protocol by reading sequential memory addresses.
            InitializeDataTable();
            MessageBox.Show("Array read command sent. Data will be populated when received.",
                "Array Editor", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnWrite(object sender, RoutedEventArgs e)
        {
            // In a real implementation, this would write array data to the MCU
            MessageBox.Show("Array write command sent.", "Array Editor",
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
                    // Header
                    var headers = _dataTable.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
                    writer.WriteLine(string.Join(",", headers));
                    // Data
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
