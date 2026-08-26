using Josha.Business;
using Josha.ViewModels;
using System;
using System.Collections.Generic;
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

        // Drives marquee selection by mutating the ListBox's native SelectedItems
        // (mirrors FileListView.ApplyProgrammaticSelection).
        private void ApplyProgrammaticSelection(Func<FileRowViewModel, bool, bool> selector)
        {
            if (DataContext is not FileListViewModel vm) return;

            foreach (var row in vm.Rows)
            {
                if (row.IsParentLink) continue;

                bool isSelected = MainList.SelectedItems.Contains(row);
                bool shouldSelect = selector(row, isSelected);

                if (shouldSelect && !isSelected)
                    MainList.SelectedItems.Add(row);
                else if (!shouldSelect && isSelected)
                    MainList.SelectedItems.Remove(row);
            }
        }

        private Point? _selectionStart;
        private bool _isSelecting;
        private HashSet<FileRowViewModel>? _selectionSnapshot;

        // Capture the mouse-down position; a marquee only makes sense starting
        // from empty tile-grid space, not from a tile itself or the scrollbar.
        private void OnListPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _selectionStart = null;

            var src = e.OriginalSource as DependencyObject;
            var container = ItemsControl.ContainerFromElement(MainList, src) as ListBoxItem;
            if (container != null) return;

            if (FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(src) == null)
                _selectionStart = e.GetPosition(MainList);
        }

        private void OnListPreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            // A plain click (not a marquee drag) on empty tile space clears
            // the selection, same as Explorer. Ctrl/Shift click on empty
            // space is left as a no-op rather than wiping the selection.
            bool plainEmptyClick = _selectionStart != null && !_isSelecting;
            EndSelectionDrag();

            if (plainEmptyClick && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
                ApplyProgrammaticSelection((row, _) => false);
        }

        private void OnListMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndSelectionDrag();
                return;
            }

            if (_selectionStart != null)
                HandleSelectionDrag(e);
        }

        // Windows-style marquee select: click-drag from empty tile-grid space
        // draws a rectangle and selects every tile it overlaps. Holding
        // Ctrl/Shift extends the pre-drag selection (toggling overlapped
        // tiles) instead of replacing it, matching Explorer.
        private void HandleSelectionDrag(MouseEventArgs e)
        {
            if (DataContext is not FileListViewModel vm) return;

            var pos = e.GetPosition(MainList);

            if (!_isSelecting)
            {
                if (Math.Abs(pos.X - _selectionStart!.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(pos.Y - _selectionStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
                    return;

                _isSelecting = true;
                _selectionSnapshot = vm.SelectedRows.ToHashSet();
                MainList.CaptureMouse();
                SelectionBox.Visibility = Visibility.Visible;
            }

            var rect = new Rect(_selectionStart!.Value, pos);
            Canvas.SetLeft(SelectionBox, rect.X);
            Canvas.SetTop(SelectionBox, rect.Y);
            SelectionBox.Width = rect.Width;
            SelectionBox.Height = rect.Height;

            bool extend = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
            ApplyProgrammaticSelection((row, _) =>
            {
                if (row.IsParentLink) return false;

                bool intersects = RowIntersects(row, rect);
                if (!extend) return intersects;

                bool wasSelected = _selectionSnapshot!.Contains(row);
                return wasSelected ^ intersects;
            });
        }

        private bool RowIntersects(FileRowViewModel row, Rect rect)
        {
            if (MainList.ItemContainerGenerator.ContainerFromItem(row) is not ListBoxItem container ||
                !container.IsVisible)
                return false;

            var topLeft = container.TransformToAncestor(MainList).Transform(new Point(0, 0));
            var bounds = new Rect(topLeft, new Size(container.ActualWidth, container.ActualHeight));
            return bounds.IntersectsWith(rect);
        }

        private void EndSelectionDrag()
        {
            _selectionStart = null;
            _selectionSnapshot = null;
            if (!_isSelecting) return;

            _isSelecting = false;
            if (MainList.IsMouseCaptured) MainList.ReleaseMouseCapture();
            SelectionBox.Visibility = Visibility.Collapsed;
        }

        private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
        {
            for (var node = start; node != null; node = System.Windows.Media.VisualTreeHelper.GetParent(node) ?? LogicalTreeParent(node))
                if (node is T t) return t;
            return null;
        }

        private static DependencyObject? LogicalTreeParent(DependencyObject node) =>
            node is FrameworkElement fe ? fe.Parent : null;
    }
}
