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
            BaudRateTextBox.Text = vm.Settings.Communication.BaudRate.ToString(CultureInfo.InvariantCulture);
            ClockTextBox.Text = vm.Settings.Communication.BaseClockMHz.ToString("F2", CultureInfo.InvariantCulture);
            UpdateRateText();
            ClockTextBox.TextChanged += (s, e) => UpdateRateText();
            BaudRateTextBox.TextChanged += (s, e) => UpdateRateText();
        }

        private void UpdateRateText()
        {
            if (int.TryParse(BaudRateTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var baudRate) &&
                baudRate > 0)
            {
                RateText.Text = $"Using baud rate: {baudRate} bps ({baudRate / 1_000_000.0:F3} Mbps)";
                return;
            }

            if (double.TryParse(ClockTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var clock) &&
                clock > 0)
            {
                double rate = clock / 8.0;
                RateText.Text = $"Using base clock: {clock:F2} MHz -> {rate:F3} Mbps";
                return;
            }

            RateText.Text = "---";
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            bool baudOk = int.TryParse(BaudRateTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var baudRate) &&
                          baudRate > 0;
            bool clockOk = double.TryParse(ClockTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var clock) &&
                           clock > 0;

            if (!baudOk && !clockOk)
            {
                MessageBox.Show("Please enter a valid baud rate or base clock.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _vm.Settings.Communication.BaudRate = baudOk ? baudRate : 0;
            _vm.Settings.Communication.BaseClockMHz = clockOk ? clock : 0;
            DialogResult = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
