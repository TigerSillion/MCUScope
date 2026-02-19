using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCUScope.Models;
using MCUScope.Services;
using System;
using System.Collections.ObjectModel;
using System.Windows;

namespace MCUScope.ViewModels
{
    public partial class CommunicationViewModel : ObservableObject
    {
        private readonly SessionState _session;

        public CommunicationViewModel()
        {
            _session = SessionState.Instance;
            _session.SerialService.ConnectionStatusChanged += OnConnectionStatusChanged;
            _session.IcsService.ConnectionLost += OnConnectionLost;
            _session.IcsService.ProtocolError += OnProtocolError;

            int saved = _session.ResolveBaudRate();
            _baudRate = saved > 0 ? saved : 2_000_000;
            RefreshPorts();
            UpdateSerialLocalStatus();
        }

        [ObservableProperty] private string _selectedPort = string.Empty;
        [ObservableProperty] private ObservableCollection<string> _availablePorts = new();
        [ObservableProperty] private string _serialLocalStatusText = "Port: -, Baud: -, Local: Disconnected";
        [ObservableProperty] private ConnectionStatus _connectionStatus = ConnectionStatus.Disconnected;
        [ObservableProperty] private string _statusText = "Disconnected";
        [ObservableProperty] private int _baudRate = 2000000;

        public ObservableCollection<int> BaudRateOptions { get; } = new()
        {
            9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600, 1000000, 2000000
        };

        partial void OnBaudRateChanged(int value)
        {
            if (value > 0)
                _session.Settings.Communication.BaudRate = value;
            UpdateSerialLocalStatus();
        }

        [RelayCommand]
        private void RefreshPorts()
        {
            AvailablePorts.Clear();
            foreach (var port in SerialCommunicationService.GetAvailablePorts())
                AvailablePorts.Add(port);

            if (!string.IsNullOrEmpty(SelectedPort) && !AvailablePorts.Contains(SelectedPort))
                SelectedPort = string.Empty;

            if (string.IsNullOrEmpty(SelectedPort))
            {
                string savedPort = _session.Settings.Communication.PortName;
                if (!string.IsNullOrWhiteSpace(savedPort) && AvailablePorts.Contains(savedPort))
                {
                    SelectedPort = savedPort;
                }
                else if (AvailablePorts.Count > 0)
                {
                    SelectedPort = AvailablePorts[0];
                }
            }

            UpdateSerialLocalStatus();
        }

        [RelayCommand]
        private void ConnectPort()
        {
            if (string.IsNullOrEmpty(SelectedPort)) return;
            int baudRate = BaudRate > 0 ? BaudRate : _session.ResolveBaudRate();
            _session.Settings.Communication.BaudRate = baudRate;
            _session.Settings.Communication.PortName = SelectedPort;
            _session.SerialService.Open(SelectedPort, baudRate);
            _session.IcsService.RequestInfo();
            _session.IcsService.StartHealthMonitor();
            UpdateSerialLocalStatus();
        }

        [RelayCommand]
        private void DisconnectPort()
        {
            _session.IcsService.StopHealthMonitor();
            _session.SerialService.Close();
            UpdateSerialLocalStatus();
        }

        private void OnConnectionStatusChanged(object? sender, ConnectionStatusChangedEventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                ConnectionStatus = e.Status;
                StatusText = e.StatusText;
                _session.ConnectionStatus = e.Status;
                _session.StatusText = e.StatusText;
                UpdateSerialLocalStatus();
            });
        }

        public void UpdateSerialLocalStatus()
        {
            var serial = _session.SerialService;
            string port = serial.IsOpen ? serial.PortName :
                (string.IsNullOrWhiteSpace(SelectedPort) ? "-" : SelectedPort);
            int baudRate = BaudRate > 0 ? BaudRate : _session.ResolveBaudRate();
            string localState = serial.IsOpen ? "Open" : "Disconnected";
            string remoteState = ConnectionStatus switch
            {
                ConnectionStatus.Connected => "MCU: Connected",
                ConnectionStatus.IcsUnitOnly => "MCU: Waiting Info",
                _ => "MCU: Disconnected"
            };
            SerialLocalStatusText = $"Port: {port}, Baud: {baudRate}, Local: {localState}, {remoteState}";
        }

        private void OnConnectionLost(object? sender, EventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                LogService.Error("Connection lost - auto disconnecting");
                _session.IcsService.StopHealthMonitor();
                _session.SerialService.Close();
                UpdateSerialLocalStatus();
                StatusText = "Connection lost (no ping response)";
            });
        }

        private void OnProtocolError(object? sender, ProtocolErrorEventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                LogService.Warn($"Protocol error: {e.Message}");
            });
        }

        partial void OnSelectedPortChanged(string value)
        {
            UpdateSerialLocalStatus();
        }
    }
}
