using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace MCUScope.Models
{
    public partial class ScopeValueItem : ObservableObject
    {
        [ObservableProperty] private string _channelId = string.Empty;
        [ObservableProperty] private bool _visible = true;
        [ObservableProperty] private Color _color = Colors.Green;
        [ObservableProperty] private string _variableName = string.Empty;
        [ObservableProperty] private double _valPerDiv = 1.0;
        [ObservableProperty] private double _position = 50.0;
        [ObservableProperty] private double _offset;
        [ObservableProperty] private double _min = double.NaN;
        [ObservableProperty] private double _max = double.NaN;
        [ObservableProperty] private double _average = double.NaN;
        [ObservableProperty] private double _cursorX1 = double.NaN;
        [ObservableProperty] private double _cursorX2 = double.NaN;
        [ObservableProperty] private double _cursorY1 = double.NaN;
        [ObservableProperty] private double _cursorY2 = double.NaN;
        [ObservableProperty] private double _x1MinusX2 = double.NaN;
        [ObservableProperty] private double _y1MinusY2 = double.NaN;
    }
}
