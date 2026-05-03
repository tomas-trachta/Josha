using Josha.Business;
using Josha.Services;
using Josha.ViewModels;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace Josha.Views
{
    public partial class FileListView : UserControl
    {
        internal event Action<FileRowViewModel>? RowActivated;
        internal event Action? NavigateUpRequested;

        public FileListView()
        {
            InitializeComponent();

            // SizeChanged on the host catches both initial layout and pane
            // resizes; MainList's own SizeChanged was unreliable on first show.
            SizeChanged += (_, _) => StretchNameColumn();
            Loaded += OnLoaded;
        }

        private bool _columnHooksAttached;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_columnHooksAttached) return;
            if (MainList.View is not GridView gv || gv.Columns.Count == 0) return;
            _columnHooksAttached = true;

            // User-resizing any other column re-flows the Name column so the
            // row keeps filling the pane.
            var widthDpd = DependencyPropertyDescriptor.FromProperty(
                GridViewColumn.WidthProperty, typeof(GridViewColumn));
            if (widthDpd != null)
            {
                for (int i = 1; i < gv.Columns.Count; i++)
                    widthDpd.AddValueChanged(gv.Columns[i], (_, _) => StretchNameColumn());
            }

            // Defer the first stretch to after layout has settled, otherwise
            // ActualWidth still reports the GridView's intrinsic content size
            // rather than the pane width.
            Dispatcher.BeginInvoke(new Action(StretchNameColumn),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void StretchNameColumn()
        {
            if (MainList.View is not GridView gv || gv.Columns.Count == 0) return;

            double host = MainList.ActualWidth;
            if (host <= 0) host = ActualWidth;
            if (host <= 0) return;

            double otherWidth = 0;
            for (int i = 1; i < gv.Columns.Count; i++)
                otherWidth += gv.Columns[i].ActualWidth;

            // Leave room for the vertical scroll bar so the last column isn't
            // pushed under it when the list overflows vertically.
            double available = host - otherWidth - SystemParameters.VerticalScrollBarWidth - 4;
            if (available < 80) available = 80;

            if (Math.Abs(gv.Columns[0].ActualWidth - available) > 0.5)
                gv.Columns[0].Width = available;
        }

        internal void FocusFilterBox()
        {
            FilterBox.Focus();
            Keyboard.Focus(FilterBox);
            FilterBox.SelectAll();
        }

        private void OnFilterKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not FileListViewModel vm) return;

            switch (e.Key)
            {
                case Key.Escape:
                    // Clear text → FilterText setter triggers RowsView.Refresh,
                    // re-showing the full list. Then focus the list so arrows work.
                    vm.FilterText = "";
                    FocusList();
                    e.Handled = true;
                    break;

                case Key.Enter:
                    // Keep filter applied; just hand focus to the list.
                    FocusList();
                    e.Handled = true;
                    break;
            }
        }

        // Focuses a concrete ListViewItem, not the ListView container — focusing
        // the container is fragile; WPF's keyboard navigation can immediately
        // hand focus to the next tab-stop sibling. Targeting a real item also
        // means arrow keys move selection from a known starting point.
        internal void FocusList()
        {
            if (MainList.Items.Count == 0)
            {
                MainList.Focus();
                Keyboard.Focus(MainList);
                return;
            }

            if (MainList.SelectedIndex < 0)
            {
                for (int i = 0; i < MainList.Items.Count; i++)
                {
                    if (MainList.Items[i] is FileRowViewModel r && !r.IsParentLink)
                    {
                        MainList.SelectedIndex = i;
                        break;
                    }
                }
                if (MainList.SelectedIndex < 0) MainList.SelectedIndex = 0;
            }

            MainList.UpdateLayout();

            var idx = MainList.SelectedIndex;
            if (MainList.ItemContainerGenerator.ContainerFromIndex(idx) is ListViewItem container)
            {
                container.Focus();
                Keyboard.Focus(container);
                return;
            }

            MainList.Focus();
            Keyboard.Focus(MainList);
        }

        private void OnHeaderClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not FileListViewModel vm) return;
            if (e.OriginalSource is not GridViewColumnHeader header) return;
            if (header.Tag is not string tag) return;

            if (Enum.TryParse<ListSortColumn>(tag, ignoreCase: true, out var col))
                vm.SetSort(col);
        }

        private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            var src = e.OriginalSource as DependencyObject;
            var container = ItemsControl.ContainerFromElement(MainList, src) as ListViewItem;
            if (container?.Content is FileRowViewModel row)
            {
                RowActivated?.Invoke(row);
                e.Handled = true;
            }
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            // Ignore typing inside the filter TextBox so Enter/Backspace don't
            // bubble up as navigation.
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

        // IsVisibleChanged (not Loaded): the TextBox loads collapsed, so the
        // Loaded path can't focus it. Background priority avoids losing focus
        // to the ListBoxItem's own selection-driven focus change.
        private void OnRenameEditorVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is not TextBox tb) return;
            if (!tb.IsVisible) return;
            if (tb.DataContext is not FileRowViewModel row) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                tb.Focus();
                Keyboard.Focus(tb);

                if (!row.IsDirectory)
                {
                    var dot = row.Name.LastIndexOf('.');
                    if (dot > 0)
                    {
                        tb.Select(0, dot);
                        return;
                    }
                }
                tb.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OnRenameEditorKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox tb) return;
            if (tb.DataContext is not FileRowViewModel row) return;
            if (DataContext is not FileListViewModel vm) return;

            switch (e.Key)
            {
                case Key.Enter:
                    _ = vm.CommitRenameAsync(row, tb.Text?.Trim() ?? "");
                    e.Handled = true;
                    break;

                case Key.Escape:
                    vm.CancelRename(row);
                    e.Handled = true;
                    break;
            }
        }

        private void OnListPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not FileListViewModel vm) return;

            var src = e.OriginalSource as DependencyObject;
            var container = ItemsControl.ContainerFromElement(MainList, src) as ListViewItem;
            if (container?.Content is not FileRowViewModel hit) return;

            // Right-click on a non-selected row replaces selection (Explorer).
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

        private Point? _dragStart;

        // Capture the mouse-down position; the actual drag starts later in
        // MouseMove only after the mouse moves past WPF's drag threshold AND
        // the user is dragging from a row (not the column header / empty area).
        private void OnListPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Only consider drags that originated on a real row.
            var src = e.OriginalSource as DependencyObject;
            var container = ItemsControl.ContainerFromElement(MainList, src) as ListViewItem;
            if (container?.Content is FileRowViewModel)
                _dragStart = e.GetPosition(null);
            else
                _dragStart = null;
        }

        private void OnListPreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _dragStart = null;
        }

        private void OnListMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragStart == null || e.LeftButton != MouseButtonState.Pressed) return;

            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            if (DataContext is not FileListViewModel vm)
            {
                _dragStart = null;
                return;
            }

            var paths = vm.SelectedRows
                .Where(r => !r.IsParentLink)
                .Select(r => r.FullPath)
                .ToArray();

            _dragStart = null;
            if (paths.Length == 0) return;

            var data = new DataObject(DataFormats.FileDrop, paths);
            try
            {
                DragDrop.DoDragDrop(MainList, data, DragDropEffects.Copy | DragDropEffects.Move);
            }
            catch (Exception ex)
            {
                Log.Warn("FileList", "Drag-drop source failed", ex);
            }
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
