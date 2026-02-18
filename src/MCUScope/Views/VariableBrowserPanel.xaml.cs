using MCUScope.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MCUScope.Views
{
    public partial class VariableBrowserPanel : UserControl
    {
        private Point _dragStartPoint;

        public VariableBrowserPanel()
        {
            InitializeComponent();
        }

        private void OnVariableGridPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(this);
        }

        private void OnVariableGridMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            var current = e.GetPosition(this);
            if (Math.Abs(current.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            if (sender is not DataGrid dg || dg.SelectedItem is not VariableBrowserItem item)
                return;

            var data = new DataObject("MCUScope.VariableName", item.Name);
            DragDrop.DoDragDrop(dg, data, DragDropEffects.Copy);
        }
    }
}
