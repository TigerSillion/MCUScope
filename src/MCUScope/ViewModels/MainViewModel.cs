using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCUScope.Models;
using MCUScope.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace MCUScope.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly SessionState _session;

        public CommunicationViewModel Communication { get; }
        public ScopeViewModel Scope { get; }
        public WatchViewModel Watch { get; }
        public FocDebugViewModel FocDebug { get; }
        public VariableBrowserViewModel VariableBrowser { get; }

        public LocalizationService Localization => LocalizationService.Instance;

        [ObservableProperty] private string _languageButtonText = "EN";

        public MainViewModel()
        {
            _session = SessionState.Instance;
            Communication = new CommunicationViewModel();
            Scope = new ScopeViewModel();
            Watch = new WatchViewModel();
            FocDebug = new FocDebugViewModel();
            VariableBrowser = new VariableBrowserViewModel();
        }

        [RelayCommand]
        private void ToggleLanguage()
        {
            Localization.ToggleLanguage();
            LanguageButtonText = Localization.LanguageDisplayText;
        }

        // ---- Project Commands ----

        [RelayCommand]
        private void NewProject()
        {
            _session.Settings = new ProjectSettings();
            _session.IcsService.Variables.Clear();
            _session.VariableNames.Clear();
            foreach (var item in Watch.WatchItems) item.Name = string.Empty;
            foreach (var item in Scope.ScopeValues) item.VariableName = string.Empty;
            _session.CurrentProjectPath = string.Empty;
            Scope.ClearChartData();
        }

        [RelayCommand]
        private void OpenProject()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "MCUScope Project (*.mcuproj)|*.mcuproj|All Files (*.*)|*.*",
                Title = "Open Project"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var settings = ProjectFileService.LoadProject(dlg.FileName);
                    _session.Settings = settings;
                    _session.CurrentProjectPath = dlg.FileName;
                    ApplySettings(settings);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to open project: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private void SaveProject()
        {
            if (string.IsNullOrEmpty(_session.CurrentProjectPath))
            {
                SaveProjectAs();
                return;
            }
            SaveProjectToFile(_session.CurrentProjectPath);
        }

        [RelayCommand]
        private void SaveProjectAs()
        {
            var dlg = new SaveFileDialog
            {
                Filter = "MCUScope Project (*.mcuproj)|*.mcuproj|All Files (*.*)|*.*",
                Title = "Save Project As"
            };
            if (dlg.ShowDialog() == true)
            {
                _session.CurrentProjectPath = dlg.FileName;
                SaveProjectToFile(dlg.FileName);
            }
        }

        private void SaveProjectToFile(string path)
        {
            try
            {
                var settings = _session.Settings;
                Scope.CollectSettings(settings);
                settings.Communication = new CommunicationSettings
                {
                    PortName = Communication.SelectedPort,
                    BaudRate = settings.Communication.BaudRate,
                    BaseClockMHz = settings.Communication.BaseClockMHz
                };
                ProjectFileService.SaveProject(path, settings);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save project: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [ObservableProperty] private bool _isLoadingVariables;

        [RelayCommand]
        private async Task LoadVariables()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Variable Files (*.csv;*.map;*.sym;*.xml)|*.csv;*.map;*.sym;*.xml|All Files (*.*)|*.*",
                Title = "Load Variables"
            };
            if (dlg.ShowDialog() == true)
                await LoadVariableFileAsync(dlg.FileName);
        }

        [RelayCommand]
        private async Task UpdateVariables()
        {
            if (!string.IsNullOrEmpty(_session.Settings.VariableFilePath) &&
                File.Exists(_session.Settings.VariableFilePath))
            {
                await LoadVariableFileAsync(_session.Settings.VariableFilePath);
            }
            else
            {
                await LoadVariables();
            }
        }

        private async Task LoadVariableFileAsync(string filePath)
        {
            if (IsLoadingVariables) return;
            IsLoadingVariables = true;
            try
            {
                var variables = await Task.Run(() => VariableFileService.LoadVariableFile(filePath));
                _session.IcsService.Variables.Clear();
                _session.IcsService.Variables.AddRange(variables);
                _session.Settings.VariableFilePath = filePath;
                _session.RebuildVariableNameList();
                FocDebug.AutoDiscoverCommand.Execute(null);
                VariableBrowser.LoadFromVariables();
                LogService.Info($"Loaded {variables.Count} variables from {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load variables: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoadingVariables = false;
            }
        }

        [RelayCommand]
        private void SaveChartData()
        {
            var dlg = new SaveFileDialog
            {
                Filter = "MCUScope Chart Data (*.dtlcd)|*.dtlcd|CSV Data (*.csv)|*.csv|All Files (*.*)|*.*",
                Title = "Save Chart Data"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var chartData = Scope.CollectChartData();
                    if (dlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    {
                        ProjectFileService.SaveChartDataCsv(dlg.FileName, chartData);
                    }
                    else
                    {
                        ProjectFileService.SaveChartData(dlg.FileName, chartData);
                        ProjectFileService.SaveChartDataCsv(dlg.FileName + ".csv", chartData);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save chart data: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private void LoadChartData()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "MCUScope Chart Data (*.dtlcd)|*.dtlcd|All Files (*.*)|*.*",
                Title = "Load Chart Data"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var chartData = ProjectFileService.LoadChartData(dlg.FileName);
                    Scope.ApplyChartData(chartData);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load chart data: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ---- Variable Settings ----

        public List<VariableInfo> GetVariableSettingsCopy()
        {
            return _session.IcsService.Variables.Select(v => v.Clone()).ToList();
        }

        public void ApplyVariableSettings(IEnumerable<VariableInfo> editedVariables)
        {
            var editedList = editedVariables.ToList();
            var editedMap = editedList.ToDictionary(
                v => $"{v.Name}@{v.Address:X8}",
                v => v,
                StringComparer.Ordinal);

            var renameMap = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var variable in _session.IcsService.Variables)
            {
                string key = $"{variable.Name}@{variable.Address:X8}";
                if (!editedMap.TryGetValue(key, out var edited)) continue;

                string oldDisplay = variable.DisplayName;
                variable.ModifiedType = edited.ModifiedType;
                variable.Scale = edited.Scale;
                variable.ReadEnabled = edited.ReadEnabled;
                variable.WriteEnabled = edited.WriteEnabled;
                variable.Alias = edited.Alias;
                variable.Comment = edited.Comment;

                string newDisplay = variable.DisplayName;
                if (!string.Equals(oldDisplay, newDisplay, StringComparison.Ordinal))
                    renameMap[oldDisplay] = newDisplay;
            }

            foreach (var watch in Watch.WatchItems)
            {
                if (!string.IsNullOrEmpty(watch.Name) && renameMap.TryGetValue(watch.Name, out var renamed))
                    watch.Name = renamed;
            }

            Scope.ApplyVariableRenames(renameMap);
            _session.RebuildVariableNameList();
        }

        public void NotifyCommunicationSettingsChanged()
        {
            Communication.UpdateSerialLocalStatus();
        }

        // ---- Settings Apply ----

        private void ApplySettings(ProjectSettings settings)
        {
            settings.Communication ??= new CommunicationSettings();

            Scope.ApplySettings(settings);
            Communication.SelectedPort = settings.Communication.PortName;

            if (!string.IsNullOrEmpty(settings.VariableFilePath) && File.Exists(settings.VariableFilePath))
                _ = LoadVariableFileAsync(settings.VariableFilePath);

            Communication.UpdateSerialLocalStatus();
        }

        public void Dispose()
        {
            Watch.StopAutoReadLoop();
            _session.Dispose();
        }
    }
}
