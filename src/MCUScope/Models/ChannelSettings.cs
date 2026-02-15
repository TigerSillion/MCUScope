using System.Windows.Media;

namespace MCUScope.Models
{
    public class ChannelSettings
    {
        public string ChannelId { get; set; } = string.Empty;
        public string VariableName { get; set; } = string.Empty;
        public double ValPerDiv { get; set; } = 1.0;
        public double Position { get; set; } = 50.0; // percent
        public double Offset { get; set; }
        public bool Visible { get; set; } = true;
        public Color Color { get; set; } = Colors.Green;
    }
}
