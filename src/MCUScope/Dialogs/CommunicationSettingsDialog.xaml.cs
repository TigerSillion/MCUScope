using MCUScope.ViewModels;
using System.Globalization;
using System.Windows;

namespace MCUScope.Dialogs
{
    public partial class CommunicationSettingsDialog : Window
    {
        private readonly MainViewModel _vm;

        public CommunicationSettingsDialog(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            ClockTextBox.Text = vm.Settings.Communication.BaseClockMHz.ToString("F2");
            UpdateRateText();
            ClockTextBox.TextChanged += (s, e) => UpdateRateText();
        }

        private void UpdateRateText()
        {
            if (double.TryParse(ClockTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var clock))
            {
                double rate = clock / 8.0;
                RateText.Text = $"{rate:F3} Mbps";
            }
            else
            {
                RateText.Text = "---";
            }
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(ClockTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var clock))
            {
                _vm.Settings.Communication.BaseClockMHz = clock;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Please enter a valid clock frequency.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
