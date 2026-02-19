using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCUScope.Models;
using MCUScope.Services;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using MathNet.Numerics.IntegralTransforms;
using Microsoft.Win32;

namespace MCUScope.ViewModels
{
    public partial class ScopeViewModel : ObservableObject
    {
        private readonly SessionState _session;
        private readonly Dictionary<int, double[]> _channelData = new();
        private readonly Dictionary<int, WaveformDataEventArgs> _pendingWaveforms = new();
        private readonly object _pendingWaveformsLock = new();
        private int _waveformUiUpdateScheduled = 0;
        private double _currentSamplePeriod = 0.0001;

        public ScopeViewModel()
        {
            _session = SessionState.Instance;
            _session.IcsService.WaveformDataReceived += OnWaveformDataReceived;

            var colors = new[] {
                System.Windows.Media.Colors.Yellow, System.Windows.Media.Colors.Cyan,
                System.Windows.Media.Colors.Magenta, System.Windows.Media.Colors.Green,
                System.Windows.Media.Colors.Orange, System.Windows.Media.Colors.White,
                System.Windows.Media.Colors.LimeGreen, System.Windows.Media.Colors.Pink,
                System.Windows.Media.Colors.LightBlue, System.Windows.Media.Colors.Red,
                System.Windows.Media.Colors.Gold, System.Windows.Media.Colors.Violet
            };

            for (int i = 0; i < 12; i++)
            {
                ScopeValues.Add(new ScopeValueItem
                {
                    ChannelId = $"M{i + 1}",
                    Color = colors[i % colors.Length],
                    Visible = i == 0,
                    ValPerDiv = 1.0,
                    Position = 50.0
                });
            }

            InitializePlotModels();

            ThemeService.Instance.ThemeChanged += OnThemeChanged;
        }

        // Collections
        public ObservableCollection<ScopeValueItem> ScopeValues { get; } = new();
        public ObservableCollection<string> VariableNames => _session.VariableNames;

        // Running state
        [ObservableProperty] private bool _isScopeRunning;
        [ObservableProperty] private string _scopeStatusText = "Stop";

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
        [ObservableProperty] private double _cursorX1Position = 0.002;
        [ObservableProperty] private double _cursorX2Position = 0.005;
        [ObservableProperty] private double _cursorY1Position = 0.0;
        [ObservableProperty] private double _cursorY2Position = 1.0;
        [ObservableProperty] private string _cursorInfoText = "X1=---s, X2=---s, dX=---s, 1/dX=---Hz";

        // Zoom settings
        [ObservableProperty] private bool _zoomCursorEnabled;
        [ObservableProperty] private double _zoomSecPerDiv = 0.0005;
        [ObservableProperty] private double _zoomPosition = 50.0;

        // FFT settings
        [ObservableProperty] private bool _fftEnabled;
        [ObservableProperty] private string _fftSource = "All";
        [ObservableProperty] private string _fftScope = "All";
        public ObservableCollection<string> FftSourceOptions { get; } = new() { "All" };
        [ObservableProperty] private string _fftWindow = "Hann";
        [ObservableProperty] private string _fftScale = "Rms";

        // Save settings
        [ObservableProperty] private bool _autoSaveEnabled;

        // Tab headers
        [ObservableProperty] private string _fftTabHeader = "FFT";

        partial void OnFftEnabledChanged(bool value)
        {
            FftTabHeader = value ? "FFT (ON)" : "FFT";
            if (value) UpdateFftChart();
        }

        // Plot models
        [ObservableProperty] private PlotModel _mainPlotModel = new();
        [ObservableProperty] private PlotModel _zoomPlotModel = new();
        [ObservableProperty] private PlotModel _fftPlotModel = new();

        private void InitializePlotModels()
        {
            MainPlotModel = CreateScopePlotModel("Scope Chart");
            ZoomPlotModel = CreateScopePlotModel("Zoom");
            FftPlotModel = CreateFftPlotModel();
        }

        private static readonly OxyColor ScopeBg = OxyColor.FromRgb(0x0A, 0x0A, 0x0A);
        private static readonly OxyColor ScopeMajorGrid = OxyColor.FromArgb(60, 0, 180, 0);
        private static readonly OxyColor ScopeMinorGrid = OxyColor.FromArgb(25, 0, 180, 0);
        private static readonly OxyColor ScopeAxisColor = OxyColor.FromRgb(0x80, 0x80, 0x80);

        private static PlotModel CreateScopePlotModel(string title)
        {
            var model = new PlotModel
            {
                Background = ScopeBg,
                PlotAreaBorderColor = OxyColors.DarkGray,
                PlotAreaBorderThickness = new OxyThickness(1),
                TextColor = ScopeAxisColor
            };

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Time (s)",
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = ScopeMajorGrid,
                MinorGridlineStyle = LineStyle.Dot,
                MinorGridlineColor = ScopeMinorGrid,
                AxislineColor = ScopeAxisColor,
                TextColor = ScopeAxisColor,
                TitleColor = ScopeAxisColor,
                TicklineColor = ScopeAxisColor
            });

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Value",
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = ScopeMajorGrid,
                MinorGridlineStyle = LineStyle.Dot,
                MinorGridlineColor = ScopeMinorGrid,
                AxislineColor = ScopeAxisColor,
                TextColor = ScopeAxisColor,
                TitleColor = ScopeAxisColor,
                TicklineColor = ScopeAxisColor
            });

            return model;
        }

        private static PlotModel CreateFftPlotModel()
        {
            var model = new PlotModel
            {
                Background = ScopeBg,
                PlotAreaBorderColor = OxyColors.DarkGray,
                PlotAreaBorderThickness = new OxyThickness(1),
                TextColor = ScopeAxisColor
            };

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Frequency (Hz)",
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = ScopeMajorGrid,
                MinorGridlineStyle = LineStyle.Dot,
                MinorGridlineColor = ScopeMinorGrid,
                AxislineColor = ScopeAxisColor,
                TextColor = ScopeAxisColor,
                TitleColor = ScopeAxisColor,
                TicklineColor = ScopeAxisColor
            });

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Magnitude",
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = ScopeMajorGrid,
                MinorGridlineStyle = LineStyle.Dot,
                MinorGridlineColor = ScopeMinorGrid,
                AxislineColor = ScopeAxisColor,
                TextColor = ScopeAxisColor,
                TitleColor = ScopeAxisColor,
                TicklineColor = ScopeAxisColor
            });

            return model;
        }

        // ---- Commands ----

        [RelayCommand]
        private void RunScope()
        {
            if (!_session.SerialService.IsOpen) return;

            var activeChannels = ScopeValues
                .Select((v, i) => new { v, i })
                .Where(x => x.v.Visible && !string.IsNullOrEmpty(x.v.VariableName))
                .Select(x =>
                {
                    if (_session.IcsService.TryResolveVariable(x.v.VariableName, out var variable))
                    {
                        if (variable.IsLikelyInternal || !variable.IsProtocolScalar)
                        {
                            LogService.Warn($"Scope channel {x.v.ChannelId} ignored: '{variable.Name}' is not a scalar protocol variable.");
                            return null;
                        }
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
            _session.IcsService.SetTrigger(trigger);
            _session.IcsService.StartScope(SamplePeriod, RecordLength, activeChannels);
            LogService.Info($"Scope start requested: channels={activeChannels.Length}, sample={SamplePeriod:G6}s, record={RecordLength}");
        }

        [RelayCommand]
        private void StopScope()
        {
            IsScopeRunning = false;
            ScopeStatusText = "Stop";
            _session.IcsService.StopScope();
        }

        // ---- Chart Data Access ----

        public ChartDataFile CollectChartData()
        {
            var chartData = new ChartDataFile { SamplePeriod = _currentSamplePeriod };
            foreach (var kvp in _channelData.OrderBy(k => k.Key))
            {
                var channel = kvp.Key < ScopeValues.Count ? ScopeValues[kvp.Key] : null;
                chartData.Channels.Add(new ChannelData
                {
                    Name = channel?.VariableName ?? $"CH{kvp.Key + 1}",
                    Data = kvp.Value
                });
            }
            return chartData;
        }

        public void ApplyChartData(ChartDataFile chartData)
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

        public void ClearChartData()
        {
            _channelData.Clear();
            MainPlotModel.Series.Clear();
            MainPlotModel.InvalidatePlot(true);
            ZoomPlotModel.Series.Clear();
            ZoomPlotModel.InvalidatePlot(true);
            FftPlotModel.Series.Clear();
            FftPlotModel.InvalidatePlot(true);
        }

        public void AutoSaveChartData()
        {
            try
            {
                string dir = Path.GetDirectoryName(_session.CurrentProjectPath) ?? Environment.CurrentDirectory;
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string path = Path.Combine(dir, $"autosave_{timestamp}.dtlcd");
                var chartData = CollectChartData();
                ProjectFileService.SaveChartData(path, chartData);
                ProjectFileService.SaveChartDataCsv(path + ".csv", chartData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AutoSave failed: {ex.Message}");
            }
        }

        // ---- Settings Collect/Apply ----

        public void CollectSettings(ProjectSettings settings)
        {
            settings.Time = new TimeSettings
            {
                Mode = TimeMode,
                SecPerDiv = SecPerDiv,
                SamplePeriod = SamplePeriod,
                RecordLength = RecordLength
            };
            settings.Trigger = new TriggerSettings
            {
                Position = TriggerPosition,
                Level = TriggerLevel,
                Source = TriggerSource,
                Mode = TriggerMode,
                Edge = TriggerEdge
            };
            settings.Zoom = new ZoomSettings
            {
                CursorEnabled = ZoomCursorEnabled,
                SecPerDiv = ZoomSecPerDiv,
                Position = ZoomPosition
            };
            settings.Cursor = new CursorSettings
            {
                CursorX1 = CursorX1Enabled,
                CursorX2 = CursorX2Enabled,
                CursorY1 = CursorY1Enabled,
                CursorY2 = CursorY2Enabled,
                CursorX1Position = CursorX1Position,
                CursorX2Position = CursorX2Position,
                CursorY1Position = CursorY1Position,
                CursorY2Position = CursorY2Position
            };
            settings.Fft = new FftSettings
            {
                Enabled = FftEnabled,
                Source = FftSource,
                Scope = FftScope,
                Window = FftWindow,
                Scale = FftScale
            };
            settings.Save = new SaveSettings { AutoSave = AutoSaveEnabled };

            settings.Channels.Clear();
            foreach (var sv in ScopeValues)
            {
                settings.Channels.Add(new ChannelSettings
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

        public void ApplySettings(ProjectSettings settings)
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
            CursorX1Position = settings.Cursor.CursorX1Position;
            CursorX2Position = settings.Cursor.CursorX2Position;
            CursorY1Position = settings.Cursor.CursorY1Position;
            CursorY2Position = settings.Cursor.CursorY2Position;

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
        }

        public void ApplyVariableRenames(Dictionary<string, string> renameMap)
        {
            foreach (var channel in ScopeValues)
            {
                if (!string.IsNullOrEmpty(channel.VariableName) &&
                    renameMap.TryGetValue(channel.VariableName, out var renamed))
                {
                    channel.VariableName = renamed;
                }
            }
        }

        // ---- Event Handlers ----

        private void OnWaveformDataReceived(object? sender, WaveformDataEventArgs e)
        {
            lock (_pendingWaveformsLock)
            {
                // Keep only the latest frame per channel to avoid UI queue flood.
                _pendingWaveforms[e.ChannelIndex] = e;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            if (Interlocked.Exchange(ref _waveformUiUpdateScheduled, 1) == 0)
            {
                dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(ProcessPendingWaveformsOnUi));
            }
        }

        private void ProcessPendingWaveformsOnUi()
        {
            Dictionary<int, WaveformDataEventArgs> snapshot;
            lock (_pendingWaveformsLock)
            {
                snapshot = new Dictionary<int, WaveformDataEventArgs>(_pendingWaveforms);
                _pendingWaveforms.Clear();
            }

            foreach (var kv in snapshot)
            {
                var frame = kv.Value;
                _channelData[frame.ChannelIndex] = frame.Data;
                _currentSamplePeriod = frame.SamplePeriod;
                UpdateScopeValues(frame.ChannelIndex, frame.Data);
            }

            if (snapshot.Count > 0)
            {
                UpdateMainChart();
                if (FftEnabled)
                    UpdateFftChart();
                UpdateZoomChart();

                if (AutoSaveEnabled)
                    AutoSaveChartData();
            }

            Interlocked.Exchange(ref _waveformUiUpdateScheduled, 0);
            lock (_pendingWaveformsLock)
            {
                if (_pendingWaveforms.Count > 0 &&
                    Interlocked.Exchange(ref _waveformUiUpdateScheduled, 1) == 0)
                {
                    Application.Current?.Dispatcher.BeginInvoke(
                        DispatcherPriority.Background,
                        new Action(ProcessPendingWaveformsOnUi));
                }
            }
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
                    StrokeThickness = 1.5,
                    TrackerFormatString = $"{channel.ChannelId} {{4:G6}}  @{{2:G6}}s",
                    Title = channel.ChannelId
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

            string header = $"{FormatTime(SecPerDiv)}/div, {FormatTime(_currentSamplePeriod)}/S, {ScopeStatusText}";
            MainPlotModel.Title = header;
            MainPlotModel.TitleFontSize = 10;
            MainPlotModel.TitleColor = OxyColors.LightGray;

            UpdateCursorAnnotations();
            UpdateFftSourceOptions();
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
                    StrokeThickness = 1.5,
                    TrackerFormatString = $"{channel.ChannelId} {{4:G6}}  @{{2:G6}}s",
                    Title = channel.ChannelId
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
                if (data.Length < 4) continue;  // not enough data for meaningful FFT

                int n = 1;
                while (n < data.Length) n <<= 1;

                var complexData = new System.Numerics.Complex[n];
                for (int i = 0; i < data.Length; i++)
                {
                    double windowVal = ApplyWindow(i, data.Length);
                    complexData[i] = new System.Numerics.Complex(data[i] * windowVal, 0);
                }

                try { Fourier.Forward(complexData, FourierOptions.NoScaling); }
                catch (Exception ex) { LogService.Warn($"FFT failed: {ex.Message}"); continue; }

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
            if (value <= 0) { SamplePeriod = 0.000001; return; }
            if (value > 10.0) { SamplePeriod = 10.0; return; }
            RecordLength = (int)(SecPerDiv * 10 / SamplePeriod) + 1;
        }

        partial void OnRecordLengthChanged(int value)
        {
            if (value < 1) RecordLength = 1;
            else if (value > 4096) RecordLength = 4096;
            // Update auto-computed SecPerDiv display
            OnPropertyChanged(nameof(SecPerDiv));
        }

        // ---- Cursor change handlers ----

        partial void OnCursorX1EnabledChanged(bool value) => UpdateCursorAnnotations();
        partial void OnCursorX2EnabledChanged(bool value) => UpdateCursorAnnotations();
        partial void OnCursorY1EnabledChanged(bool value) => UpdateCursorAnnotations();
        partial void OnCursorY2EnabledChanged(bool value) => UpdateCursorAnnotations();
        partial void OnCursorX1PositionChanged(double value) => UpdateCursorAnnotations();
        partial void OnCursorX2PositionChanged(double value) => UpdateCursorAnnotations();
        partial void OnCursorY1PositionChanged(double value) => UpdateCursorAnnotations();
        partial void OnCursorY2PositionChanged(double value) => UpdateCursorAnnotations();

        private void UpdateCursorAnnotations()
        {
            // Remove old cursor annotations
            MainPlotModel.Annotations.Clear();

            var cursorColor = OxyColor.FromArgb(180, 255, 255, 0);

            if (CursorX1Enabled)
            {
                MainPlotModel.Annotations.Add(new LineAnnotation
                {
                    Type = LineAnnotationType.Vertical,
                    X = CursorX1Position,
                    Color = cursorColor,
                    StrokeThickness = 1,
                    LineStyle = LineStyle.Dash,
                    Text = $"X1={FormatTime(CursorX1Position)}",
                    TextColor = cursorColor,
                    FontSize = 9
                });
            }

            if (CursorX2Enabled)
            {
                MainPlotModel.Annotations.Add(new LineAnnotation
                {
                    Type = LineAnnotationType.Vertical,
                    X = CursorX2Position,
                    Color = OxyColor.FromArgb(180, 0, 255, 255),
                    StrokeThickness = 1,
                    LineStyle = LineStyle.Dash,
                    Text = $"X2={FormatTime(CursorX2Position)}",
                    TextColor = OxyColor.FromArgb(180, 0, 255, 255),
                    FontSize = 9
                });
            }

            if (CursorY1Enabled)
            {
                MainPlotModel.Annotations.Add(new LineAnnotation
                {
                    Type = LineAnnotationType.Horizontal,
                    Y = CursorY1Position,
                    Color = OxyColor.FromArgb(180, 255, 128, 0),
                    StrokeThickness = 1,
                    LineStyle = LineStyle.Dash,
                    Text = $"Y1={CursorY1Position:G4}",
                    TextColor = OxyColor.FromArgb(180, 255, 128, 0),
                    FontSize = 9
                });
            }

            if (CursorY2Enabled)
            {
                MainPlotModel.Annotations.Add(new LineAnnotation
                {
                    Type = LineAnnotationType.Horizontal,
                    Y = CursorY2Position,
                    Color = OxyColor.FromArgb(180, 128, 255, 0),
                    StrokeThickness = 1,
                    LineStyle = LineStyle.Dash,
                    Text = $"Y2={CursorY2Position:G4}",
                    TextColor = OxyColor.FromArgb(180, 128, 255, 0),
                    FontSize = 9
                });
            }

            // Update cursor info text
            UpdateCursorInfoText();

            // Update per-channel cursor values
            UpdateChannelCursorValues();

            MainPlotModel.InvalidatePlot(true);
        }

        private void UpdateCursorInfoText()
        {
            string x1 = CursorX1Enabled ? FormatTime(CursorX1Position) : "---";
            string x2 = CursorX2Enabled ? FormatTime(CursorX2Position) : "---";
            string dxStr = "---";
            string freqStr = "---";

            if (CursorX1Enabled && CursorX2Enabled)
            {
                double dx = CursorX2Position - CursorX1Position;
                dxStr = FormatTime(Math.Abs(dx));
                if (dx != 0)
                    freqStr = $"{Math.Abs(1.0 / dx):G4}Hz";
            }

            string y1 = CursorY1Enabled ? $"{CursorY1Position:G4}" : "---";
            string y2 = CursorY2Enabled ? $"{CursorY2Position:G4}" : "---";
            string dyStr = "---";
            if (CursorY1Enabled && CursorY2Enabled)
                dyStr = $"{Math.Abs(CursorY2Position - CursorY1Position):G4}";

            CursorInfoText = $"X1={x1}, X2={x2}, dX={dxStr}, 1/dX={freqStr}  |  Y1={y1}, Y2={y2}, dY={dyStr}";
        }

        private void UpdateChannelCursorValues()
        {
            foreach (var kvp in _channelData)
            {
                if (kvp.Key >= ScopeValues.Count) continue;
                var channel = ScopeValues[kvp.Key];
                var data = kvp.Value;

                if (CursorX1Enabled)
                {
                    int idx = (int)(CursorX1Position / _currentSamplePeriod);
                    channel.CursorX1 = (idx >= 0 && idx < data.Length) ? data[idx] : double.NaN;
                }
                else channel.CursorX1 = double.NaN;

                if (CursorX2Enabled)
                {
                    int idx = (int)(CursorX2Position / _currentSamplePeriod);
                    channel.CursorX2 = (idx >= 0 && idx < data.Length) ? data[idx] : double.NaN;
                }
                else channel.CursorX2 = double.NaN;

                channel.X1MinusX2 = (!double.IsNaN(channel.CursorX1) && !double.IsNaN(channel.CursorX2))
                    ? channel.CursorX1 - channel.CursorX2 : double.NaN;

                channel.CursorY1 = CursorY1Enabled ? CursorY1Position : double.NaN;
                channel.CursorY2 = CursorY2Enabled ? CursorY2Position : double.NaN;
                channel.Y1MinusY2 = (CursorY1Enabled && CursorY2Enabled)
                    ? CursorY1Position - CursorY2Position : double.NaN;
            }
        }

        // ---- FFT Source Options Update ----

        private void UpdateFftSourceOptions()
        {
            var current = FftSource;
            FftSourceOptions.Clear();
            FftSourceOptions.Add("All");
            foreach (var sv in ScopeValues)
            {
                if (sv.Visible && !string.IsNullOrEmpty(sv.VariableName))
                    FftSourceOptions.Add(sv.ChannelId);
            }
            FftSource = FftSourceOptions.Contains(current) ? current : "All";
        }

        // ---- Screenshot ----

        [RelayCommand]
        private void SaveScreenshot()
        {
            var dlg = new SaveFileDialog
            {
                Filter = "PNG Image (*.png)|*.png|SVG Vector (*.svg)|*.svg",
                Title = "Save Scope Screenshot",
                FileName = $"MCUScope_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                OxyPlot.IExporter exporter;
                if (dlg.FileName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                    exporter = new OxyPlot.SvgExporter { Width = 1280, Height = 720 };
                else
                    exporter = new OxyPlot.Wpf.PngExporter { Width = 1280, Height = 720 };

                using var stream = File.Create(dlg.FileName);
                exporter.Export(MainPlotModel, stream);
                LogService.Info($"Screenshot saved: {Path.GetFileName(dlg.FileName)}");
            }
            catch (Exception ex)
            {
                LogService.Error($"Screenshot failed: {ex.Message}");
            }
        }

        // ---- Theme Change Handler ----

        private void OnThemeChanged(object? sender, EventArgs e)
        {
            // Avoid heavy synchronous plot-model rebuild during theme switching.
            // Scope plot palette is currently fixed oscilloscope style, so only
            // request lightweight redraw to keep UI responsive.
            Application.Current?.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    MainPlotModel.InvalidatePlot(false);
                    ZoomPlotModel.InvalidatePlot(false);
                    FftPlotModel.InvalidatePlot(false);
                }));
        }
    }
}
