using MCUScope.Models;
using MCUScope.ViewModels;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

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
            _editCopy = vm.GetVariableSettingsCopy();

            VariableGrid.ItemsSource = _editCopy;
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            // Commit current in-place edit before applying back to ViewModel.
            VariableGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            VariableGrid.CommitEdit(DataGridEditingUnit.Row, true);
            _vm.ApplyVariableSettings(_editCopy);
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
