using System.Collections.Generic;

namespace MCUScope.Models
{
    public class TimeSettings
    {
        public string Mode { get; set; } = "Buffer"; // Buffer or Roll
        public double SecPerDiv { get; set; } = 0.001;   // 1ms/div default
        public double SamplePeriod { get; set; } = 0.0001; // 100us default
        public int RecordLength { get; set; } = 101;
    }

    public class ZoomSettings
    {
        public bool CursorEnabled { get; set; }
        public double SecPerDiv { get; set; } = 0.0005;
        public double Position { get; set; } = 50.0;
    }

    public class CursorSettings
    {
        public bool CursorX1 { get; set; }
        public bool CursorX2 { get; set; }
        public bool CursorY1 { get; set; }
        public bool CursorY2 { get; set; }
        public double CursorX1Position { get; set; } = 0.002;
        public double CursorX2Position { get; set; } = 0.005;
        public double CursorY1Position { get; set; }
        public double CursorY2Position { get; set; } = 1.0;
    }

    public class FftSettings
    {
        public bool Enabled { get; set; }
        public string Source { get; set; } = "All";
        public string Scope { get; set; } = "All";
        public string Window { get; set; } = "Hann";
        public string Scale { get; set; } = "Rms";
    }

    public class SaveSettings
    {
        public bool AutoSave { get; set; }
    }

    public class CommunicationSettings
    {
        public string PortName { get; set; } = string.Empty;
        public int BaudRate { get; set; } = 1000000; // 1Mbps default
        public double BaseClockMHz { get; set; } = 8.0;
    }

    public class ProjectSettings
    {
        public int Version { get; set; } = 2;
        public string ThemeName { get; set; } = "Dark";
        public string VariableFilePath { get; set; } = string.Empty;
        public TimeSettings Time { get; set; } = new();
        public TriggerSettings Trigger { get; set; } = new();
        public ZoomSettings Zoom { get; set; } = new();
        public CursorSettings Cursor { get; set; } = new();
        public FftSettings Fft { get; set; } = new();
        public SaveSettings Save { get; set; } = new();
        public CommunicationSettings Communication { get; set; } = new();
        public List<ChannelSettings> Channels { get; set; } = new();
    }
}
