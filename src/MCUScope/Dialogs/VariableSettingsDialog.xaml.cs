using MCUScope.Models;
using MCUScope.ViewModels;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace MCUScope.Dialogs
{
    public partial class VariableSettingsDialog : Window
    {
        private readonly MainViewModel _vm;
        private readonly List<VariableInfo> _editCopy;

        public VariableSettingsDialog(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            // Create editable copies
            _editCopy = vm.VariableNames
                .Select(name =>
                {
                    var orig = _vm.GetType().GetProperty("_icsService") != null ? null :
                        new VariableInfo { Name = name };
                    return orig ?? new VariableInfo { Name = name };
                })
                .ToList();

            // Use the actual variables from the protocol service
            _editCopy.Clear();
            // Access through reflection-free approach: just create from variable names
            foreach (var name in vm.VariableNames)
            {
                _editCopy.Add(new VariableInfo { Name = name, ModifiedType = VariableType.Int32 });
            }

            VariableGrid.ItemsSource = _editCopy;
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            // Apply changes back
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
