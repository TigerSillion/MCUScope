using CommunityToolkit.Mvvm.ComponentModel;

namespace MCUScope.Models
{
    public partial class WatchItem : ObservableObject
    {
        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private bool _readEnabled;
        [ObservableProperty] private string _readValue = string.Empty;
        [ObservableProperty] private bool _writeEnabled;
        [ObservableProperty] private string _writeValue = string.Empty;
    }
}
