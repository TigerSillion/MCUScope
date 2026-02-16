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
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };

            var varCombo = new ComboBox { Width = 100, Margin = new Thickness(4, 0, 4, 0) };
            foreach (var name in _vm.VariableNames)
                varCombo.Items.Add(name);

            var slider = new Slider { Width = 200, Minimum = -1000, Maximum = 1000, Margin = new Thickness(4, 0, 4, 0) };
            var valueText = new TextBlock { Width = 60, VerticalAlignment = VerticalAlignment.Center };

            slider.ValueChanged += (s, ev) =>
            {
                valueText.Text = ev.NewValue.ToString("G4");
            };

            var writeBtn = new Button { Content = "Write", Width = 50, Margin = new Thickness(4, 0, 4, 0) };
            writeBtn.Click += (s, ev) =>
            {
                if (varCombo.SelectedItem is string varName && !string.IsNullOrEmpty(varName))
                {
                    // Write slider value to variable
                    // _icsService.RequestWriteVariable(varName, slider.Value);
                }
            };

            var removeBtn = new Button { Content = "X", Width = 24, Margin = new Thickness(4, 0, 0, 0) };
            removeBtn.Click += (s, ev) => ControlPanel.Children.Remove(panel);

            panel.Children.Add(varCombo);
            panel.Children.Add(slider);
            panel.Children.Add(valueText);
            panel.Children.Add(writeBtn);
            panel.Children.Add(removeBtn);

            ControlPanel.Children.Add(panel);
        }

        private void OnAddToggle(object sender, RoutedEventArgs e)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };

            var varCombo = new ComboBox { Width = 100, Margin = new Thickness(4, 0, 4, 0) };
            foreach (var name in _vm.VariableNames)
                varCombo.Items.Add(name);

            var toggle = new ToggleButton { Content = "OFF", Width = 60, Margin = new Thickness(4, 0, 4, 0) };
            toggle.Checked += (s, ev) =>
            {
                toggle.Content = "ON";
                if (varCombo.SelectedItem is string varName && !string.IsNullOrEmpty(varName))
                {
                    // Write 1 to variable
                }
            };
            toggle.Unchecked += (s, ev) =>
            {
                toggle.Content = "OFF";
                if (varCombo.SelectedItem is string varName && !string.IsNullOrEmpty(varName))
                {
                    // Write 0 to variable
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
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };

            var varCombo = new ComboBox { Width = 100, Margin = new Thickness(4, 0, 4, 0) };
            foreach (var name in _vm.VariableNames)
                varCombo.Items.Add(name);

            var valueText = new TextBlock
            {
                Text = "---",
                Width = 100,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(4, 0, 4, 0)
            };

            var readBtn = new Button { Content = "Read", Width = 50, Margin = new Thickness(4, 0, 4, 0) };
            readBtn.Click += (s, ev) =>
            {
                if (varCombo.SelectedItem is string varName && !string.IsNullOrEmpty(varName))
                {
                    // Read variable value
                    // _icsService.RequestReadVariable(varName);
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
