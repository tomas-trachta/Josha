using Josha.Models;
using Josha.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Josha.Views
{
    public partial class CommandPalette : Window
    {
        private readonly CommandPaletteViewModel _vm;

        internal CommandPalette(IEnumerable<CommandPaletteItem> items)
        {
            InitializeComponent();
            _vm = new CommandPaletteViewModel(items);
            DataContext = _vm;
            Loaded += (_, _) =>
            {
                QueryBox.Focus();
                Keyboard.Focus(QueryBox);
            };
        }

        private void OnQueryKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                    MoveSelection(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    MoveSelection(-1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    Execute(_vm.Selected);
                    e.Handled = true;
                    break;
            }
        }

        private void MoveSelection(int delta)
        {
            if (_vm.FilteredItems.Count == 0) return;
            var idx = _vm.Selected == null ? -1 : _vm.FilteredItems.IndexOf(_vm.Selected);
            idx = (idx + delta + _vm.FilteredItems.Count) % _vm.FilteredItems.Count;
            _vm.Selected = _vm.FilteredItems[idx];
            ResultsList.ScrollIntoView(_vm.Selected);
        }

        private void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var src = e.OriginalSource as DependencyObject;
            var container = ItemsControl.ContainerFromElement(ResultsList, src) as ListBoxItem;
            if (container?.Content is CommandPaletteItem item)
            {
                Execute(item);
                e.Handled = true;
            }
        }

        // Close before invoking so own-modal actions don't overlap the palette.
        private void Execute(CommandPaletteItem? item)
        {
            if (item?.Action == null) return;
            DialogResult = true;
            var action = item.Action;
            Close();
            Dispatcher.BeginInvoke(action);
        }

        private void OnCancelCommand(object sender, ExecutedRoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
