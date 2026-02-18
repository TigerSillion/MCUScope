using CommunityToolkit.Mvvm.ComponentModel;
using MCUScope.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MCUScope.Services
{
    public partial class SessionState : ObservableObject
    {
        private static SessionState? _instance;
        public static SessionState Instance => _instance ??= new SessionState();

        private SessionState()
        {
            SerialService = new SerialCommunicationService();
            IcsService = new IcsProtocolService(SerialService);
        }

        public SerialCommunicationService SerialService { get; }
        public IcsProtocolService IcsService { get; }

        [ObservableProperty] private ProjectSettings _settings = new();
        [ObservableProperty] private ConnectionStatus _connectionStatus = ConnectionStatus.Disconnected;
        [ObservableProperty] private string _statusText = "Disconnected";
        [ObservableProperty] private string _currentProjectPath = string.Empty;

        public ObservableCollection<string> VariableNames { get; } = new();

        public void RebuildVariableNameList()
        {
            VariableNames.Clear();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var variable in IcsService.Variables)
            {
                if (variable.IsLikelyInternal || !variable.IsProtocolScalar)
                    continue;
                if (unique.Add(variable.DisplayName))
                {
                    VariableNames.Add(variable.DisplayName);
                }
            }
        }

        public int ResolveBaudRate()
        {
            int baudRate = Settings.Communication.BaudRate > 0
                ? Settings.Communication.BaudRate
                : (int)Math.Round(Settings.Communication.BaseClockMHz * 1_000_000 / 8.0);

            if (baudRate <= 0)
                baudRate = 1_000_000;

            return baudRate;
        }

        public void Dispose()
        {
            IcsService.Dispose();
            SerialService.Dispose();
        }
    }
}
