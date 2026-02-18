using MCUScope.Services;
using MCUScope.ViewModels;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MCUScope.Dialogs
{
    public partial class CustomControlPanelDialog : Window
    {
        private readonly MainViewModel _vm;

        public CustomControlPanelDialog(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
        }

        private void OnAddSlider(object sender, RoutedEventArgs e)
        {
            var session = SessionState.Instance;
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };

            var varCombo = new ComboBox { Width = 100, Margin = new Thickness(4, 0, 4, 0) };
            foreach (var name in session.VariableNames)
                varCombo.Items.Add(name);

            var minBox = new TextBox { Width = 50, Text = "-1000", Margin = new Thickness(2, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
            var maxBox = new TextBox { Width = 50, Text = "1000", Margin = new Thickness(2, 0, 4, 0), VerticalContentAlignment = VerticalAlignment.Center };
            var slider = new Slider { Width = 150, Minimum = -1000, Maximum = 1000, Margin = new Thickness(4, 0, 4, 0) };
            var valueText = new TextBlock { Width = 60, VerticalAlignment = VerticalAlignment.Center };

            minBox.LostFocus += (s, ev) =>
            {
                if (double.TryParse(minBox.Text, out var min)) slider.Minimum = min;
            };
            maxBox.LostFocus += (s, ev) =>
            {
                if (double.TryParse(maxBox.Text, out var max)) slider.Maximum = max;
            };

            slider.ValueChanged += (s, ev) =>
            {
                valueText.Text = ev.NewValue.ToString("G4");
            };

            var writeBtn = new Button { Content = "Write", Width = 50, Margin = new Thickness(4, 0, 4, 0) };
            writeBtn.Click += (s, ev) =>
            {
                if (varCombo.SelectedItem is string varName && !string.IsNullOrEmpty(varName))
                {
                    session.IcsService.RequestWriteVariable(varName, slider.Value);
                    LogService.Info($"CustomPanel: Write {varName} = {slider.Value:G4}");
                }
            };

            var removeBtn = new Button { Content = "X", Width = 24, Margin = new Thickness(4, 0, 0, 0) };
            removeBtn.Click += (s, ev) => ControlPanel.Children.Remove(panel);

            panel.Children.Add(varCombo);
            panel.Children.Add(minBox);
            panel.Children.Add(maxBox);
            panel.Children.Add(slider);
            panel.Children.Add(valueText);
            panel.Children.Add(writeBtn);
            panel.Children.Add(removeBtn);

            ControlPanel.Children.Add(panel);
        }

        private void OnAddToggle(object sender, RoutedEventArgs e)
        {
            var session = SessionState.Instance;
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };

            var varCombo = new ComboBox { Width = 100, Margin = new Thickness(4, 0, 4, 0) };
            foreach (var name in session.VariableNames)
                varCombo.Items.Add(name);

            var toggle = new ToggleButton { Content = "OFF", Width = 60, Margin = new Thickness(4, 0, 4, 0) };
            toggle.Checked += (s, ev) =>
            {
                toggle.Content = "ON";
                if (varCombo.SelectedItem is string varName && !string.IsNullOrEmpty(varName))
                {
                    session.IcsService.RequestWriteVariable(varName, 1.0);
                    LogService.Info($"CustomPanel: Toggle {varName} = ON");
                }
            };
            toggle.Unchecked += (s, ev) =>
            {
                toggle.Content = "OFF";
                if (varCombo.SelectedItem is string varName && !string.IsNullOrEmpty(varName))
                {
                    session.IcsService.RequestWriteVariable(varName, 0.0);
                    LogService.Info($"CustomPanel: Toggle {varName} = OFF");
                }
            };

            var removeBtn = new Button { Content = "X", Width = 24, Margin = new Thickness(4, 0, 0, 0) };
            removeBtn.Click += (s, ev) => ControlPanel.Children.Remove(panel);

            panel.Children.Add(varCombo);
            panel.Children.Add(toggle);
            panel.Children.Add(removeBtn);

            ControlPanel.Children.Add(panel);
        }

        private void OnAddDisplay(object sender, RoutedEventArgs e)
        {
            var session = SessionState.Instance;
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };

            var varCombo = new ComboBox { Width = 100, Margin = new Thickness(4, 0, 4, 0) };
            foreach (var name in session.VariableNames)
                varCombo.Items.Add(name);

            var valueText = new TextBlock
            {
                Text = "---",
                Width = 100,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(4, 0, 4, 0)
            };

            // Subscribe to variable read events to update this display
            session.IcsService.VariableValueReceived += (s2, ev2) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (varCombo.SelectedItem is string varName)
                    {
                        if (string.Equals(ev2.VariableName, varName, System.StringComparison.Ordinal))
                            valueText.Text = ev2.Value.ToString("G6");
                        else if (session.IcsService.TryResolveVariable(varName, out var vi) &&
                                 string.Equals(vi.Name, ev2.VariableName, System.StringComparison.Ordinal))
                            valueText.Text = ev2.Value.ToString("G6");
                    }
                });
            };

            var readBtn = new Button { Content = "Read", Width = 50, Margin = new Thickness(4, 0, 4, 0) };
            readBtn.Click += (s, ev) =>
            {
                if (varCombo.SelectedItem is string varName && !string.IsNullOrEmpty(varName))
                {
                    session.IcsService.RequestReadVariable(varName);
                }
            };

            var removeBtn = new Button { Content = "X", Width = 24, Margin = new Thickness(4, 0, 0, 0) };
            removeBtn.Click += (s, ev) => ControlPanel.Children.Remove(panel);

            panel.Children.Add(varCombo);
            panel.Children.Add(valueText);
            panel.Children.Add(readBtn);
            panel.Children.Add(removeBtn);

            ControlPanel.Children.Add(panel);
        }

        private void OnClearAll(object sender, RoutedEventArgs e)
        {
            ControlPanel.Children.Clear();
        }
    }
}
