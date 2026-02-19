using MCUScope.Dialogs;
using MCUScope.Models;
using MCUScope.Services;
using MCUScope.ViewModels;
using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MCUScope.Views
{
    public partial class MainWindow : Window
    {
        private bool _isThemeSwitching;

        public MainWindow()
        {
            InitializeComponent();

            // Populate theme combo
            foreach (var theme in ThemeService.AvailableThemes)
                ThemeCombo.Items.Add(theme);
            ThemeCombo.SelectedItem = ThemeService.Instance.CurrentTheme;

            // Auto-scroll log to bottom
            ((INotifyCollectionChanged)LogService.LogEntries).CollectionChanged += (s, e) =>
            {
                if (LogListBox.Items.Count > 0)
                    LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
            };

            // Update connection status dot color
            SessionState.Instance.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SessionState.ConnectionStatus))
                {
                    string brushKey = SessionState.Instance.ConnectionStatus switch
                    {
                        ConnectionStatus.Connected    => "SuccessBrush",
                        ConnectionStatus.IcsUnitOnly  => "WarningBrush",
                        _                             => "TextMutedBrush",   // Disconnected
                    };
                    ConnDot.Fill = (Brush)FindResource(brushKey);
                }
            };

            Closing += (s, e) =>
            {
                if (DataContext is MainViewModel vm)
                    vm.Dispose();
            };
        }

        private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isThemeSwitching) return;
            if (ThemeCombo.SelectedItem is not string themeName) return;

            _isThemeSwitching = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    ThemeService.Instance.ApplyTheme(themeName);
                    if (DataContext is MainViewModel vm)
                        SessionState.Instance.Settings.ThemeName = themeName;
                }
                finally
                {
                    _isThemeSwitching = false;
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OnPortComboDropDownOpened(object sender, EventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.Communication.RefreshPortsCommand.Execute(null);
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
                "MCUScope  v1.0.0\n" +
                "Real-time variable waveform viewer for embedded MCUs\n" +
                "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                "Architecture\n" +
                "  Frontend:  WPF (.NET 8), MVVM pattern\n" +
                "  Backend:   ICS++ serial protocol (UART)\n" +
                "  Chart:     OxyPlot (phosphor-scope rendering)\n" +
                "  Math:      MathNet.Numerics (FFT, statistics)\n\n" +
                "Features\n" +
                "  • Up to 12 simultaneous oscilloscope channels\n" +
                "  • Variable watch panel with auto-read loop\n" +
                "  • MAP / CSV / SYM variable file loading\n" +
                "  • Trigger: edge, level, source selection\n" +
                "  • Zoom window & FFT analysis\n" +
                "  • Array editor, timetable player\n" +
                "  • FOC debug panel (sensorless motor control)\n" +
                "  • Project save/load (.mcuproj)\n" +
                "  • Dark / Monokai / Solarized / Nord / Light themes\n" +
                "  • English / Chinese UI\n\n" +
                "Open Source Libraries (all MIT)\n" +
                "  OxyPlot  •  MathNet.Numerics\n" +
                "  CommunityToolkit.Mvvm  •  System.IO.Ports",
                "About MCUScope",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void OnChineseManual(object sender, RoutedEventArgs e)
        {
            // Search from exe location upward (dev + installed layouts)
            string baseDir = AppContext.BaseDirectory;
            string[] candidatePaths =
            {
                Path.Combine(baseDir, "docs", "GUI_OPERATION_MANUAL_CN.md"),
                Path.Combine(baseDir, "GUI_OPERATION_MANUAL_CN.md"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "docs", "GUI_OPERATION_MANUAL_CN.md")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "docs", "GUI_OPERATION_MANUAL_CN.md")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "docs", "GUI_OPERATION_MANUAL_CN.md")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "docs", "GUI_OPERATION_MANUAL_CN.md")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "docs", "GUI_OPERATION_MANUAL_CN.md")),
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

        // --- Log panel ---
        private void OnClearLog(object sender, RoutedEventArgs e)
        {
            LogService.ClearLogEntries();
        }

        // --- Drag-and-Drop: Watch DataGrid (drop target) ---

        private void OnWatchGridDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent("MCUScope.VariableName")
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnWatchGridDrop(object sender, DragEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (!e.Data.GetDataPresent("MCUScope.VariableName")) return;
            string varName = (string)e.Data.GetData("MCUScope.VariableName");
            if (string.IsNullOrEmpty(varName)) return;

            // Find the row under the cursor
            if (sender is DataGrid dg)
            {
                var hit = dg.InputHitTest(e.GetPosition(dg)) as DependencyObject;
                WatchItem? target = null;
                while (hit != null)
                {
                    if (hit is DataGridRow row && row.Item is WatchItem wi)
                    { target = wi; break; }
                    hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
                }
                if (target != null)
                {
                    target.Name = varName;
                    target.ReadEnabled = true;
                    LogService.Info($"Dropped '{varName}' into watch slot");
                    e.Handled = true;
                    return;
                }
            }

            // Fallback: first empty watch slot
            var empty = vm.Watch.WatchItems.FirstOrDefault(w => string.IsNullOrEmpty(w.Name));
            if (empty != null)
            {
                empty.Name = varName;
                empty.ReadEnabled = true;
                LogService.Info($"Dropped '{varName}' into watch");
            }
            e.Handled = true;
        }

        // --- Watch context menu ---
        private void OnWatchRemove(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && sender is MenuItem mi &&
                mi.Parent is ContextMenu ctx && ctx.PlacementTarget is DataGrid dg &&
                dg.SelectedItem is WatchItem item)
            {
                item.Name = string.Empty;
                item.ReadValue = string.Empty;
                item.WriteValue = string.Empty;
                item.ReadEnabled = false;
                item.WriteEnabled = false;
            }
        }

        private void OnWatchAddToScope(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && sender is MenuItem mi &&
                mi.Parent is ContextMenu ctx && ctx.PlacementTarget is DataGrid dg &&
                dg.SelectedItem is WatchItem item && !string.IsNullOrEmpty(item.Name))
            {
                if (!vm.Scope.VariableNames.Contains(item.Name))
                {
                    LogService.Warn($"Cannot add '{item.Name}' to scope: variable is not scope-bindable.");
                    return;
                }
                var emptyChannel = vm.Scope.ScopeValues.FirstOrDefault(sv => string.IsNullOrEmpty(sv.VariableName));
                if (emptyChannel != null)
                {
                    emptyChannel.VariableName = item.Name;
                    emptyChannel.Visible = true;
                    LogService.Info($"Added '{item.Name}' to scope channel {emptyChannel.ChannelId}");
                }
            }
        }

        private void OnWatchCopyValue(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi &&
                mi.Parent is ContextMenu ctx && ctx.PlacementTarget is DataGrid dg &&
                dg.SelectedItem is WatchItem item && !string.IsNullOrEmpty(item.ReadValue))
            {
                try { Clipboard.SetText(item.ReadValue); }
                catch { /* clipboard may be locked by another process */ }
            }
        }

        // --- Scope channel context menu ---
        private void OnChannelRemoveBinding(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi &&
                mi.Parent is ContextMenu ctx && ctx.PlacementTarget is DataGrid dg &&
                dg.SelectedItem is ScopeValueItem item)
            {
                item.VariableName = string.Empty;
                item.Visible = false;
            }
        }

        private void OnChannelSetTrigger(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && sender is MenuItem mi &&
                mi.Parent is ContextMenu ctx && ctx.PlacementTarget is DataGrid dg &&
                dg.SelectedItem is ScopeValueItem item)
            {
                // Map channel to trigger source
                int idx = vm.Scope.ScopeValues.IndexOf(item);
                if (idx >= 0 && idx < 12)
                {
                    vm.Scope.TriggerSource = (TriggerSource)(idx + 1); // M1=1, M2=2, etc.
                    LogService.Info($"Trigger source set to {item.ChannelId}");
                }
            }
        }

        private void OnScopeSaveScreenshot(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.Scope.SaveScreenshotCommand.Execute(null);
        }

        private void OnScopeChannelGridDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("MCUScope.VariableName"))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void OnScopeChannelGridDrop(object sender, DragEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (!e.Data.GetDataPresent("MCUScope.VariableName")) return;

            string variableName = (string)e.Data.GetData("MCUScope.VariableName");
            // Accept any variable that exists in the loaded variable list.
            // Non-scalar variables will emit a warning when scope is started (RunScope).
            if (!SessionState.Instance.IcsService.TryResolveVariable(variableName, out _))
            {
                LogService.Warn($"Drop rejected: '{variableName}' not found in loaded variables.");
                return;
            }

            ScopeValueItem? target = null;
            if (sender is DataGrid dg)
            {
                DependencyObject? source = e.OriginalSource as DependencyObject;
                var row = FindParent<DataGridRow>(source);
                if (row?.Item is ScopeValueItem rowItem)
                    target = rowItem;
            }

            target ??= vm.Scope.ScopeValues.FirstOrDefault(ch => string.IsNullOrEmpty(ch.VariableName));
            if (target == null)
            {
                LogService.Warn("Drop rejected: no available scope channel.");
                return;
            }

            target.VariableName = variableName;
            target.Visible = true;
            LogService.Info($"Drag-drop bound '{variableName}' -> {target.ChannelId}");
        }

        private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T found)
                    return found;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }
    }
}
