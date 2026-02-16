using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCUScope.Models;
using MCUScope.Services;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace MCUScope.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly SerialCommunicationService _serialService;
        private readonly IcsProtocolService _icsService;
        private Timer? _autoReadTimer;
        private string _currentProjectPath = string.Empty;

        // Scope state
        private readonly Dictionary<int, double[]> _channelData = new();
        private double _currentSamplePeriod = 0.0001;

        public MainViewModel()
        {
            _serialService = new SerialCommunicationService();
            _icsService = new IcsProtocolService(_serialService);

            _serialService.ConnectionStatusChanged += OnConnectionStatusChanged;
            _icsService.WaveformDataReceived += OnWaveformDataReceived;
            _icsService.VariableValueReceived += OnVariableValueReceived;

            // Initialize channels (M1-M12)
            var colors = new[] { Colors.Green, Colors.Red, Colors.Blue, Colors.Yellow,
                Colors.Cyan, Colors.Magenta, Colors.Orange, Colors.White,
                Colors.LimeGreen, Colors.Pink, Colors.LightBlue, Colors.Gold };

            for (int i = 0; i < 12; i++)
            {
                var ch = new ScopeValueItem
                {
                    ChannelId = $"M{i + 1}",
                    Color = colors[i % colors.Length],
                    Visible = i == 0,
                    ValPerDiv = 1.0,
                    Position = 50.0
                };
                ScopeValues.Add(ch);
            }

            // Initialize watch items (24 max)
            for (int i = 0; i < 24; i++)
            {
                WatchItems.Add(new WatchItem());
            }

            // Initialize plot models
            InitializePlotModels();

            // Initialize available COM ports
            RefreshPorts();

            // Default settings
            Settings = new ProjectSettings();
        }

        // Properties
        [ObservableProperty] private string _statusText = "Disconnected";
        [ObservableProperty] private ConnectionStatus _connectionStatus = ConnectionStatus.Disconnected;
        [ObservableProperty] private ProjectSettings _settings = new();

        // Time settings
        [ObservableProperty] private string _timeMode = "Buffer";
        [ObservableProperty] private double _secPerDiv = 0.001;
        [ObservableProperty] private double _samplePeriod = 0.0001;
        [ObservableProperty] private int _recordLength = 101;

        // Trigger settings
        [ObservableProperty] private double _triggerPosition;
        [ObservableProperty] private double _triggerLevel;
        [ObservableProperty] private TriggerSource _triggerSource = TriggerSource.EXT;
        [ObservableProperty] private TriggerMode _triggerMode = TriggerMode.Single;
        [ObservableProperty] private TriggerEdge _triggerEdge = TriggerEdge.Rise;

        // Cursor settings
        [ObservableProperty] private bool _cursorX1Enabled;
        [ObservableProperty] private bool _cursorX2Enabled;
        [ObservableProperty] private bool _cursorY1Enabled;
        [ObservableProperty] private bool _cursorY2Enabled;

        // Zoom settings
        [ObservableProperty] private bool _zoomCursorEnabled;
        [ObservableProperty] private double _zoomSecPerDiv = 0.0005;
        [ObservableProperty] private double _zoomPosition = 50.0;

        // FFT settings
        [ObservableProperty] private bool _fftEnabled;
        [ObservableProperty] private string _fftSource = "All";
        [ObservableProperty] private string _fftScope = "All";
        [ObservableProperty] private string _fftWindow = "Hann";
        [ObservableProperty] private string _fftScale = "Rms";

        // Save settings
        [ObservableProperty] private bool _autoSaveEnabled;

        // Watch settings
        [ObservableProperty] private int _autoReadInterval = 1;
        [ObservableProperty] private bool _isAutoReading;

        // COM port
        [ObservableProperty] private string _selectedPort = string.Empty;
        [ObservableProperty] private ObservableCollection<string> _availablePorts = new();

        // Cursor values display
        [ObservableProperty] private string _cursorInfoText = "X1=---s, X2=---s, dX=---s, 1/dX=---Hz";

        // Plot models
        [ObservableProperty] private PlotModel _mainPlotModel = new();
        [ObservableProperty] private PlotModel _zoomPlotModel = new();
        [ObservableProperty] private PlotModel _fftPlotModel = new();

        // Collections
        public ObservableCollection<ScopeValueItem> ScopeValues { get; } = new();
        public ObservableCollection<WatchItem> WatchItems { get; } = new();
        public ObservableCollection<string> VariableNames { get; } = new();

        // Running state
        [ObservableProperty] private bool _isScopeRunning;
        [ObservableProperty] private string _scopeStatusText = "Stop";

        private void InitializePlotModels()
        {
            // Main scope chart
            MainPlotModel = CreateScopePlotModel("Scope Chart");
            ZoomPlotModel = CreateScopePlotModel("Zoom");
            FftPlotModel = CreateFftPlotModel();
        }

        private PlotModel CreateScopePlotModel(string title)
        {
            var model = new PlotModel
            {
                Background = OxyColors.Black,
                PlotAreaBorderColor = OxyColors.DarkGray,
                TextColor = OxyColors.LightGray
            };

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Time (s)",
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromRgb(40, 40, 40),
                MinorGridlineStyle = LineStyle.Dot,
                MinorGridlineColor = OxyColor.FromRgb(30, 30, 30),
                AxislineColor = OxyColors.Gray,
                TextColor = OxyColors.LightGray,
                TitleColor = OxyColors.LightGray,
                TicklineColor = OxyColors.Gray
            });

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Value",
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromRgb(40, 40, 40),
                MinorGridlineStyle = LineStyle.Dot,
                MinorGridlineColor = OxyColor.FromRgb(30, 30, 30),
                AxislineColor = OxyColors.Gray,
                TextColor = OxyColors.LightGray,
                TitleColor = OxyColors.LightGray,
                TicklineColor = OxyColors.Gray
            });

            return model;
        }

        private PlotModel CreateFftPlotModel()
        {
            var model = new PlotModel
            {
                Background = OxyColors.Black,
                PlotAreaBorderColor = OxyColors.DarkGray,
                TextColor = OxyColors.LightGray
            };

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Frequency (Hz)",
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromRgb(40, 40, 40),
                AxislineColor = OxyColors.Gray,
                TextColor = OxyColors.LightGray,
                TitleColor = OxyColors.LightGray
            });

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Magnitude",
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromRgb(40, 40, 40),
                AxislineColor = OxyColors.Gray,
                TextColor = OxyColors.LightGray,
                TitleColor = OxyColors.LightGray
            });

            return model;
        }

        // ---- Commands ----

        [RelayCommand]
        private void RunScope()
        {
            if (!_serialService.IsOpen) return;

            var activeChannels = ScopeValues
                .Select((v, i) => new { v, i })
                .Where(x => x.v.Visible && !string.IsNullOrEmpty(x.v.VariableName))
                .Select(x =>
                {
                    if (_icsService.TryResolveVariable(x.v.VariableName, out var variable))
                    {
                        return new ScopeChannelRequest
                        {
                            ChannelIndex = x.i,
                            Address = variable.Address,
                            Type = variable.ModifiedType
                        };
                    }

                    return null;
                })
                .Where(ch => ch != null)
                .Select(ch => ch!)
                .ToArray();

            if (activeChannels.Length == 0)
            {
                MessageBox.Show("No valid scope variable selected. Please load variable file and assign visible channels.",
                    "Scope", MessageBoxButton.OK, MessageBoxImage.Warning);
                IsScopeRunning = false;
                ScopeStatusText = "Stop";
                return;
            }

            IsScopeRunning = true;
            ScopeStatusText = "Run";

            var trigger = new TriggerSettings
            {
                Position = TriggerPosition,
                Level = TriggerLevel,
                Source = TriggerSource,
                Mode = TriggerMode,
                Edge = TriggerEdge
            };
            _icsService.SetTrigger(trigger);
            _icsService.StartScope(SamplePeriod, RecordLength, activeChannels);
        }

        [RelayCommand]
        private void StopScope()
        {
            IsScopeRunning = false;
            ScopeStatusText = "Stop";
            _icsService.StopScope();
        }

        [RelayCommand]
        private void NewProject()
        {
            Settings = new ProjectSettings();
            _icsService.Variables.Clear();
            VariableNames.Clear();
            foreach (var item in WatchItems) item.Name = string.Empty;
            foreach (var item in ScopeValues) item.VariableName = string.Empty;
            _currentProjectPath = string.Empty;
            ClearChartData();
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
                    Settings = ProjectFileService.LoadProject(dlg.FileName);
                    _currentProjectPath = dlg.FileName;
                    ApplySettings(Settings);
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
            if (string.IsNullOrEmpty(_currentProjectPath))
            {
                SaveProjectAs();
                return;
            }
            SaveProjectToFile(_currentProjectPath);
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
                _currentProjectPath = dlg.FileName;
                SaveProjectToFile(dlg.FileName);
            }
        }

        private void SaveProjectToFile(string path)
        {
            try
            {
                CollectSettings();
                ProjectFileService.SaveProject(path, Settings);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save project: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void LoadVariables()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Variable Files (*.csv;*.map;*.sym;*.xml)|*.csv;*.map;*.sym;*.xml|All Files (*.*)|*.*",
                Title = "Load Variables"
            };
            if (dlg.ShowDialog() == true)
            {
                LoadVariableFile(dlg.FileName);
            }
        }

        [RelayCommand]
        private void UpdateVariables()
        {
            if (!string.IsNullOrEmpty(Settings.VariableFilePath) && File.Exists(Settings.VariableFilePath))
            {
                LoadVariableFile(Settings.VariableFilePath);
            }
            else
            {
                LoadVariables();
            }
        }

        private void LoadVariableFile(string filePath)
        {
            try
            {
                var variables = VariableFileService.LoadVariableFile(filePath);
                _icsService.Variables.Clear();
                _icsService.Variables.AddRange(variables);
                Settings.VariableFilePath = filePath;

                VariableNames.Clear();
                foreach (var v in variables)
                    VariableNames.Add(v.DisplayName);

                MessageBox.Show("Variable information has been loaded.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load variables: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
                    var chartData = CollectChartData();
                    if (dlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    {
                        ProjectFileService.SaveChartDataCsv(dlg.FileName, chartData);
                    }
                    else
                    {
                        ProjectFileService.SaveChartData(dlg.FileName, chartData);
                        // Also save CSV alongside
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
                    ApplyChartData(chartData);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load chart data: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private void ReadWatchVariables()
        {
            foreach (var item in WatchItems)
            {
                if (item.ReadEnabled && !string.IsNullOrEmpty(item.Name))
                {
                    _icsService.RequestReadVariable(item.Name);
                }
            }
        }

        [RelayCommand]
        private void WriteWatchVariables()
        {
            foreach (var item in WatchItems)
            {
                if (item.WriteEnabled && !string.IsNullOrEmpty(item.Name) &&
                    double.TryParse(item.WriteValue, out var value))
                {
                    _icsService.RequestWriteVariable(item.Name, value);
                }
            }
        }

        [RelayCommand]
        private void ToggleAutoRead()
        {
            if (IsAutoReading)
            {
                _autoReadTimer?.Dispose();
                _autoReadTimer = null;
                IsAutoReading = false;
            }
            else
            {
                IsAutoReading = true;
                _autoReadTimer = new Timer(_ =>
                {
                    Application.Current?.Dispatcher.Invoke(ReadWatchVariables);
                }, null, 0, AutoReadInterval * 1000);
            }
        }

        [RelayCommand]
        private void RefreshPorts()
        {
            AvailablePorts.Clear();
            foreach (var port in SerialCommunicationService.GetAvailablePorts())
                AvailablePorts.Add(port);
        }

        [RelayCommand]
        private void ConnectPort()
        {
            if (string.IsNullOrEmpty(SelectedPort)) return;
            int baudRate = Settings.Communication.BaudRate > 0
                ? Settings.Communication.BaudRate
                : (int)(Settings.Communication.BaseClockMHz * 1_000_000 / 8);
            _serialService.Open(SelectedPort, baudRate);
            _icsService.RequestInfo();
        }

        [RelayCommand]
        private void DisconnectPort()
        {
            StopScope();
            _serialService.Close();
        }

        // ---- Event Handlers ----

        private void OnConnectionStatusChanged(object? sender, ConnectionStatusChangedEventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                ConnectionStatus = e.Status;
                StatusText = e.StatusText;
            });
        }

        private void OnWaveformDataReceived(object? sender, WaveformDataEventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _channelData[e.ChannelIndex] = e.Data;
                _currentSamplePeriod = e.SamplePeriod;
                UpdateMainChart();
                if (FftEnabled)
                    UpdateFftChart();
                UpdateZoomChart();
                UpdateScopeValues(e.ChannelIndex, e.Data);

                if (AutoSaveEnabled)
                    AutoSaveChartData();
            });
        }

        private void OnVariableValueReceived(object? sender, VariableReadEventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                var watchItem = WatchItems.FirstOrDefault(w =>
                {
                    if (string.Equals(w.Name, e.VariableName, StringComparison.Ordinal))
                    {
                        return true;
                    }

                    if (_icsService.TryResolveVariable(w.Name, out var variable))
                    {
                        return string.Equals(variable.Name, e.VariableName, StringComparison.Ordinal);
                    }

                    return false;
                });
                if (watchItem != null)
                {
                    watchItem.ReadValue = e.Value.ToString("G6");
                }
            });
        }

        // ---- Chart Update Methods ----

        private void UpdateMainChart()
        {
            MainPlotModel.Series.Clear();

            var timeAxis = MainPlotModel.Axes[0];
            timeAxis.Minimum = 0;
            timeAxis.Maximum = SecPerDiv * 10;

            foreach (var kvp in _channelData)
            {
                if (kvp.Key >= ScopeValues.Count) continue;
                var channel = ScopeValues[kvp.Key];
                if (!channel.Visible) continue;

                var series = new LineSeries
                {
                    Color = OxyColor.FromArgb(channel.Color.A, channel.Color.R, channel.Color.G, channel.Color.B),
                    StrokeThickness = 1.5
                };

                for (int i = 0; i < kvp.Value.Length; i++)
                {
                    double time = i * _currentSamplePeriod;
                    double value = (kvp.Value[i] + channel.Offset) / channel.ValPerDiv;
                    double yPos = (channel.Position / 100.0 - 0.5) * 10 + value;
                    series.Points.Add(new DataPoint(time, yPos));
                }

                MainPlotModel.Series.Add(series);
            }

            // Update header text
            string header = $"{FormatTime(SecPerDiv)}/div, {FormatTime(_currentSamplePeriod)}/S, {ScopeStatusText}";
            MainPlotModel.Title = header;
            MainPlotModel.TitleFontSize = 10;
            MainPlotModel.TitleColor = OxyColors.LightGray;

            MainPlotModel.InvalidatePlot(true);
        }

        private void UpdateZoomChart()
        {
            ZoomPlotModel.Series.Clear();

            double zoomCenter = ZoomPosition / 100.0 * SecPerDiv * 10;
            double zoomWidth = ZoomSecPerDiv * 10;
            double zoomStart = zoomCenter - zoomWidth / 2;
            double zoomEnd = zoomCenter + zoomWidth / 2;

            var timeAxis = ZoomPlotModel.Axes[0];
            timeAxis.Minimum = zoomStart;
            timeAxis.Maximum = zoomEnd;

            foreach (var kvp in _channelData)
            {
                if (kvp.Key >= ScopeValues.Count) continue;
                var channel = ScopeValues[kvp.Key];
                if (!channel.Visible) continue;

                var series = new LineSeries
                {
                    Color = OxyColor.FromArgb(channel.Color.A, channel.Color.R, channel.Color.G, channel.Color.B),
                    StrokeThickness = 1.5
                };

                for (int i = 0; i < kvp.Value.Length; i++)
                {
                    double time = i * _currentSamplePeriod;
                    if (time < zoomStart || time > zoomEnd) continue;
                    double value = (kvp.Value[i] + channel.Offset) / channel.ValPerDiv;
                    double yPos = (channel.Position / 100.0 - 0.5) * 10 + value;
                    series.Points.Add(new DataPoint(time, yPos));
                }

                ZoomPlotModel.Series.Add(series);
            }

            ZoomPlotModel.InvalidatePlot(true);
        }

        private void UpdateFftChart()
        {
            FftPlotModel.Series.Clear();

            foreach (var kvp in _channelData)
            {
                if (kvp.Key >= ScopeValues.Count) continue;
                var channel = ScopeValues[kvp.Key];
                if (!channel.Visible) continue;
                if (FftSource != "All" && channel.ChannelId != FftSource) continue;

                var data = kvp.Value;
                // Pad to power of 2
                int n = 1;
                while (n < data.Length) n <<= 1;

                var complexData = new System.Numerics.Complex[n];
                for (int i = 0; i < data.Length; i++)
                {
                    double windowVal = ApplyWindow(i, data.Length);
                    complexData[i] = new System.Numerics.Complex(data[i] * windowVal, 0);
                }

                Fourier.Forward(complexData, FourierOptions.NoScaling);

                double freqRes = 1.0 / (_currentSamplePeriod * n);
                int halfN = n / 2;

                var series = new LineSeries
                {
                    Color = OxyColor.FromArgb(channel.Color.A, channel.Color.R, channel.Color.G, channel.Color.B),
                    StrokeThickness = 1.5
                };

                for (int i = 0; i < halfN; i++)
                {
                    double freq = i * freqRes;
                    double magnitude = complexData[i].Magnitude / data.Length;
                    if (FftScale == "Rms")
                        magnitude *= Math.Sqrt(2);
                    else if (FftScale == "dB")
                        magnitude = 20 * Math.Log10(Math.Max(magnitude, 1e-10));

                    series.Points.Add(new DataPoint(freq, magnitude));
                }

                FftPlotModel.Series.Add(series);
            }

            FftPlotModel.InvalidatePlot(true);
        }

        private double ApplyWindow(int index, int length)
        {
            return FftWindow switch
            {
                "Hann" => 0.5 * (1 - Math.Cos(2 * Math.PI * index / (length - 1))),
                "Hamming" => 0.54 - 0.46 * Math.Cos(2 * Math.PI * index / (length - 1)),
                "Blackman" => 0.42 - 0.5 * Math.Cos(2 * Math.PI * index / (length - 1)) +
                              0.08 * Math.Cos(4 * Math.PI * index / (length - 1)),
                "Rectangular" => 1.0,
                _ => 0.5 * (1 - Math.Cos(2 * Math.PI * index / (length - 1)))
            };
        }

        private void UpdateScopeValues(int channelIndex, double[] data)
        {
            if (channelIndex >= ScopeValues.Count || data.Length == 0) return;
            var sv = ScopeValues[channelIndex];
            sv.Min = data.Min();
            sv.Max = data.Max();
            sv.Average = data.Average();
        }

        private void ClearChartData()
        {
            _channelData.Clear();
            MainPlotModel.Series.Clear();
            MainPlotModel.InvalidatePlot(true);
            ZoomPlotModel.Series.Clear();
            ZoomPlotModel.InvalidatePlot(true);
            FftPlotModel.Series.Clear();
            FftPlotModel.InvalidatePlot(true);
        }

        private ChartDataFile CollectChartData()
        {
            var chartData = new ChartDataFile { SamplePeriod = _currentSamplePeriod };
            foreach (var kvp in _channelData.OrderBy(k => k.Key))
            {
                var channel = kvp.Key < ScopeValues.Count ? ScopeValues[kvp.Key] : null;
                chartData.Channels.Add(new Services.ChannelData
                {
                    Name = channel?.VariableName ?? $"CH{kvp.Key + 1}",
                    Data = kvp.Value
                });
            }
            return chartData;
        }

        private void ApplyChartData(ChartDataFile chartData)
        {
            _channelData.Clear();
            _currentSamplePeriod = chartData.SamplePeriod;
            for (int i = 0; i < chartData.Channels.Count; i++)
            {
                _channelData[i] = chartData.Channels[i].Data;
                if (i < ScopeValues.Count)
                {
                    ScopeValues[i].VariableName = chartData.Channels[i].Name;
                    ScopeValues[i].Visible = true;
                }
            }
            UpdateMainChart();
            UpdateZoomChart();
            if (FftEnabled) UpdateFftChart();
        }

        private void AutoSaveChartData()
        {
            try
            {
                string dir = Path.GetDirectoryName(_currentProjectPath) ?? Environment.CurrentDirectory;
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string path = Path.Combine(dir, $"autosave_{timestamp}.dtlcd");
                var chartData = CollectChartData();
                ProjectFileService.SaveChartData(path, chartData);
                ProjectFileService.SaveChartDataCsv(path + ".csv", chartData);
            }
            catch { }
        }

        private void CollectSettings()
        {
            Settings.Time = new TimeSettings
            {
                Mode = TimeMode,
                SecPerDiv = SecPerDiv,
                SamplePeriod = SamplePeriod,
                RecordLength = RecordLength
            };
            Settings.Trigger = new TriggerSettings
            {
                Position = TriggerPosition,
                Level = TriggerLevel,
                Source = TriggerSource,
                Mode = TriggerMode,
                Edge = TriggerEdge
            };
            Settings.Zoom = new ZoomSettings
            {
                CursorEnabled = ZoomCursorEnabled,
                SecPerDiv = ZoomSecPerDiv,
                Position = ZoomPosition
            };
            Settings.Cursor = new CursorSettings
            {
                CursorX1 = CursorX1Enabled,
                CursorX2 = CursorX2Enabled,
                CursorY1 = CursorY1Enabled,
                CursorY2 = CursorY2Enabled
            };
            Settings.Fft = new FftSettings
            {
                Enabled = FftEnabled,
                Source = FftSource,
                Scope = FftScope,
                Window = FftWindow,
                Scale = FftScale
            };
            Settings.Save = new SaveSettings { AutoSave = AutoSaveEnabled };

            Settings.Channels.Clear();
            foreach (var sv in ScopeValues)
            {
                Settings.Channels.Add(new ChannelSettings
                {
                    ChannelId = sv.ChannelId,
                    VariableName = sv.VariableName,
                    ValPerDiv = sv.ValPerDiv,
                    Position = sv.Position,
                    Offset = sv.Offset,
                    Visible = sv.Visible,
                    Color = sv.Color
                });
            }
        }

        private void ApplySettings(ProjectSettings settings)
        {
            TimeMode = settings.Time.Mode;
            SecPerDiv = settings.Time.SecPerDiv;
            SamplePeriod = settings.Time.SamplePeriod;
            RecordLength = settings.Time.RecordLength;

            TriggerPosition = settings.Trigger.Position;
            TriggerLevel = settings.Trigger.Level;
            TriggerSource = settings.Trigger.Source;
            TriggerMode = settings.Trigger.Mode;
            TriggerEdge = settings.Trigger.Edge;

            ZoomCursorEnabled = settings.Zoom.CursorEnabled;
            ZoomSecPerDiv = settings.Zoom.SecPerDiv;
            ZoomPosition = settings.Zoom.Position;

            CursorX1Enabled = settings.Cursor.CursorX1;
            CursorX2Enabled = settings.Cursor.CursorX2;
            CursorY1Enabled = settings.Cursor.CursorY1;
            CursorY2Enabled = settings.Cursor.CursorY2;

            FftEnabled = settings.Fft.Enabled;
            FftSource = settings.Fft.Source;
            FftScope = settings.Fft.Scope;
            FftWindow = settings.Fft.Window;
            FftScale = settings.Fft.Scale;

            AutoSaveEnabled = settings.Save.AutoSave;

            for (int i = 0; i < settings.Channels.Count && i < ScopeValues.Count; i++)
            {
                var ch = settings.Channels[i];
                ScopeValues[i].VariableName = ch.VariableName;
                ScopeValues[i].ValPerDiv = ch.ValPerDiv;
                ScopeValues[i].Position = ch.Position;
                ScopeValues[i].Offset = ch.Offset;
                ScopeValues[i].Visible = ch.Visible;
                ScopeValues[i].Color = ch.Color;
            }

            if (!string.IsNullOrEmpty(settings.VariableFilePath) && File.Exists(settings.VariableFilePath))
            {
                LoadVariableFile(settings.VariableFilePath);
            }
        }

        public static string FormatTime(double seconds)
        {
            if (seconds >= 1) return $"{seconds:G4}s";
            if (seconds >= 0.001) return $"{seconds * 1000:G4}ms";
            if (seconds >= 0.000001) return $"{seconds * 1000000:G4}us";
            return $"{seconds * 1000000000:G4}ns";
        }

        partial void OnSecPerDivChanged(double value)
        {
            RecordLength = (int)(SecPerDiv * 10 / SamplePeriod) + 1;
        }

        partial void OnSamplePeriodChanged(double value)
        {
            RecordLength = (int)(SecPerDiv * 10 / SamplePeriod) + 1;
        }

        public void Dispose()
        {
            _autoReadTimer?.Dispose();
            _icsService.Dispose();
            _serialService.Dispose();
        }
    }
}
