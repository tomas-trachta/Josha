using Josha.Business;
using Josha.ViewModels;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace Josha.Views
{
    public partial class FileTilesView : UserControl
    {
        internal event Action<FileRowViewModel>? RowActivated;
        internal event Action? NavigateUpRequested;

        public FileTilesView()
        {
            InitializeComponent();
        }

        private void OnTileDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            var src = e.OriginalSource as DependencyObject;
            var container = ItemsControl.ContainerFromElement(MainList, src) as ListBoxItem;
            if (container?.Content is FileRowViewModel row)
            {
                RowActivated?.Invoke(row);
                e.Handled = true;
            }
        }

        private void OnTileKeyDown(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox) return;
            if (DataContext is not FileListViewModel) return;

            switch (e.Key)
            {
                case Key.Enter:
                    if (MainList.SelectedItem is FileRowViewModel selected)
                    {
                        RowActivated?.Invoke(selected);
                        e.Handled = true;
                    }
                    break;

                case Key.Back:
                    NavigateUpRequested?.Invoke();
                    e.Handled = true;
                    break;
            }
        }

        private void OnTilePreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not FileListViewModel vm) return;

            var src = e.OriginalSource as DependencyObject;
            var container = ItemsControl.ContainerFromElement(MainList, src) as ListBoxItem;
            if (container?.Content is not FileRowViewModel hit) return;

            if (!hit.IsSelected)
            {
                foreach (var row in vm.SelectedRows.ToList())
                    row.IsSelected = false;
                hit.IsSelected = true;
            }

            var paths = vm.SelectedRows
                .Where(r => !r.IsParentLink)
                .Select(r => r.FullPath)
                .ToList();
            if (paths.Count == 0) paths = new List<string> { hit.FullPath };

            var hwnd = (PresentationSource.FromVisual(this) as HwndSource)?.Handle ?? IntPtr.Zero;
            var screen = MainList.PointToScreen(e.GetPosition(MainList));
            ShellContextMenuComponent.Show(paths, hwnd, (int)screen.X, (int)screen.Y);
            e.Handled = true;
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not FileListViewModel vm) return;

            foreach (var removed in e.RemovedItems)
                if (removed is FileRowViewModel r)
                    vm.SelectedRows.Remove(r);

            foreach (var added in e.AddedItems)
                if (added is FileRowViewModel r && !r.IsParentLink && !vm.SelectedRows.Contains(r))
                    vm.SelectedRows.Add(r);
        }
    }
}
