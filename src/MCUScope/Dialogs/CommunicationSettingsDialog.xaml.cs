using MCUScope.ViewModels;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MCUScope.Dialogs
{
    public partial class CommunicationSettingsDialog : Window
    {
        private readonly MainViewModel _vm;
        private static readonly int[] CommonBaudRates =
        {
            9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600,
            1000000, 1500000, 2000000, 3000000
        };

        public CommunicationSettingsDialog(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;

            foreach (int baud in CommonBaudRates)
            {
                BaudRateComboBox.Items.Add(baud.ToString(CultureInfo.InvariantCulture));
            }

            BaudRateComboBox.Text = vm.Settings.Communication.BaudRate.ToString(CultureInfo.InvariantCulture);
            ClockTextBox.Text = vm.Settings.Communication.BaseClockMHz.ToString("F2", CultureInfo.InvariantCulture);
            UpdateRateText();
            ClockTextBox.TextChanged += (s, e) => UpdateRateText();
            BaudRateComboBox.AddHandler(TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler(OnBaudRateTextChanged));
            BaudRateComboBox.SelectionChanged += (s, e) => UpdateRateText();
            BaudRateComboBox.LostKeyboardFocus += (s, e) => UpdateRateText();
        }

        private void OnBaudRateTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateRateText();
        }

        private void UpdateRateText()
        {
            if (int.TryParse(BaudRateComboBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var baudRate) &&
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
            bool baudOk = int.TryParse(BaudRateComboBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var baudRate) &&
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
            _vm.NotifyCommunicationSettingsChanged();
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
