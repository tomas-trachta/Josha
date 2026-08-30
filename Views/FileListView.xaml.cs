using Josha.Business;
using Josha.Services;
using Josha.ViewModels;
using System;
using System.Collections.Generic;
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
            DataContextChanged += OnDataContextChanged;
        }

        private FileListViewModel? _wiredVm;

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_wiredVm != null)
                _wiredVm.ProgrammaticSelectionRequested = null;

            if (DataContext is FileListViewModel vm)
            {
                vm.ProgrammaticSelectionRequested = ApplyProgrammaticSelection;
                _wiredVm = vm;
            }
            else
            {
                _wiredVm = null;
            }
        }

        // Drives invert / pattern selection by mutating the ListView's native
        // SelectedItems. Going through row.IsSelected silently skips virtualized
        // rows; manipulating SelectedItems updates the real selection state and
        // lets OnSelectionChanged propagate the result back into the VM.
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

        private bool _columnHooksAttached;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_columnHooksAttached) return;
            if (MainList.View is not GridView gv || gv.Columns.Count == 0) return;
            _columnHooksAttached = true;

            // User-resizing any other column re-flows the Name column so the
            // row keeps filling the pane. Hooked to DragCompleted (not the
            // Width property changing on every drag tick) because Name sits
            // to the left of every other column: rebalancing it mid-drag
            // shifts their on-screen position by the same delta the user is
            // dragging, fighting the cursor. Waiting for the gripper drag to
            // finish keeps the live resize 1:1 and only reflows Name once,
            // after the fact.
            MainList.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,
                new System.Windows.Controls.Primitives.DragCompletedEventHandler((_, _) => StretchNameColumn()));

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

        // Fires both when the user clicks away and when Enter/Escape already
        // hid the TextBox (Visibility flips to Collapsed on IsEditing=false).
        // CancelRename is idempotent, so the latter case is a harmless no-op.
        private void OnRenameEditorLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb) return;
            if (tb.DataContext is not FileRowViewModel row) return;
            if (DataContext is not FileListViewModel vm) return;

            vm.CancelRename(row);
        }

        private void OnNoteIndicatorClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not FileRowViewModel row) return;
            if (DataContext is not FileListViewModel vm) return;
            if (Window.GetWindow(this)?.DataContext is not AppShellViewModel shell) return;

            foreach (var s in vm.SelectedRows.ToList())
                s.IsSelected = false;
            row.IsSelected = true;

            if (shell.AddOrEditNoteCommand.CanExecute(null))
                shell.AddOrEditNoteCommand.Execute(null);

            e.Handled = true;
        }

        private void OnListPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not FileListViewModel vm) return;

            var src = e.OriginalSource as DependencyObject;

            // Right-click on the column header: leave it for the header's own
            // handling (no shell menu, no directory menu).
            if (FindAncestor<System.Windows.Controls.GridViewColumnHeader>(src) != null) return;

            var container = ItemsControl.ContainerFromElement(MainList, src) as ListViewItem;
            if (container?.Content is FileRowViewModel hit)
            {
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
                return;
            }

            // Empty space inside the list — show the directory action menu.
            ShowDirectoryContextMenu(vm);
            e.Handled = true;
        }

        private void ShowDirectoryContextMenu(FileListViewModel vm)
        {
            var shell = Window.GetWindow(this)?.DataContext as AppShellViewModel;
            if (shell == null) return;

            var menu = new ContextMenu
            {
                PlacementTarget = MainList,
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
            };

            AddMenuCommand(menu, "_New folder",         "F7",     shell.MkdirCommand);
            AddMenuCommand(menu, "New _file",           null,     shell.NewFileCommand);
            AddMenuCommand(menu, "Cop_y",               "Ctrl+C", shell.ClipboardCopyCommand);
            AddMenuCommand(menu, "Cu_t",                "Ctrl+X", shell.ClipboardCutCommand);
            AddMenuCommand(menu, "_Paste",              "Ctrl+V", shell.PasteCommand);
            menu.Items.Add(new Separator());
            AddMenuCommand(menu, "_Refresh",            null,     shell.RefreshActiveCommand);
            AddMenuCommand(menu, "_Select by pattern…", "+",      shell.SelectByPatternCommand);
            AddMenuCommand(menu, "_Invert selection",   "*",      shell.InvertSelectionCommand);

            if (!vm.FileSystem.IsRemote)
            {
                menu.Items.Add(new Separator());
                AddMenuCommand(menu, "Add/edit _note…", "Ctrl+Alt+N", shell.AddOrEditNoteCommand);
            }

            // "Open in Explorer" only makes sense for local paths; remote
            // panes have no shell-resolvable location.
            if (!vm.FileSystem.IsRemote && !string.IsNullOrEmpty(vm.CurrentPath))
            {
                menu.Items.Add(new Separator());
                var open = new MenuItem { Header = "Open in E_xplorer" };
                var dir = vm.CurrentPath;
                open.Click += (_, _) =>
                {
                    try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true }); }
                    catch (Exception ex) { Log.Warn("FileList", "Open in Explorer failed", ex); }
                };
                menu.Items.Add(open);
            }

            menu.IsOpen = true;
        }

        private static void AddMenuCommand(ContextMenu menu, string header, string? gesture, ICommand command)
        {
            menu.Items.Add(new MenuItem
            {
                Header = header,
                Command = command,
                InputGestureText = gesture ?? string.Empty,
            });
        }

        private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
        {
            for (var node = start; node != null; node = System.Windows.Media.VisualTreeHelper.GetParent(node) ?? LogicalTreeParent(node))
                if (node is T t) return t;
            return null;
        }

        private static DependencyObject? LogicalTreeParent(DependencyObject node) =>
            node is FrameworkElement fe ? fe.Parent : null;

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
            {
                _dragStart = e.GetPosition(null);
                _selectionStart = null;
            }
            else
            {
                _dragStart = null;

                // A marquee only makes sense starting from empty list space —
                // not from the header row or the scrollbar.
                if (FindAncestor<GridViewColumnHeader>(src) == null &&
                    FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(src) == null)
                {
                    _selectionStart = e.GetPosition(MainList);
                }
            }

            // Clicking the empty area below/beside the rows doesn't move
            // keyboard focus off the rename TextBox (nothing focusable is
            // there to take it), so LostFocus never fires. Cancel explicitly.
            if (container == null && DataContext is FileListViewModel vm)
            {
                var editing = vm.Rows.FirstOrDefault(r => r.IsEditing);
                if (editing != null) vm.CancelRename(editing);
            }
        }

        private void OnListPreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _dragStart = null;

            // A plain click (not a marquee drag) on empty list space clears
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
            {
                HandleSelectionDrag(e);
                return;
            }

            if (_dragStart == null) return;

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

        private Point? _selectionStart;
        private bool _isSelecting;
        private HashSet<FileRowViewModel>? _selectionSnapshot;

        // Windows-style marquee select: click-drag from empty list space draws
        // a rectangle and selects every row it overlaps. Holding Ctrl/Shift
        // extends the pre-drag selection (toggling overlapped rows) instead
        // of replacing it, matching Explorer.
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
            if (MainList.ItemContainerGenerator.ContainerFromItem(row) is not ListViewItem container ||
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

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not FileListViewModel vm) return;

            // Mirror both vm.SelectedRows AND row.IsSelected from the native
            // selection. The TwoWay IsSelected style binding only fires for
            // realized containers, so a shift+click that spans virtualized
            // rows would otherwise leave row.IsSelected stale — when the user
            // later scrolls those rows into view, the stale source (false)
            // can override the visually-selected container.
            foreach (var removed in e.RemovedItems)
                if (removed is FileRowViewModel r)
                {
                    r.IsSelected = false;
                    vm.SelectedRows.Remove(r);
                }

            foreach (var added in e.AddedItems)
                if (added is FileRowViewModel r && !r.IsParentLink)
                {
                    r.IsSelected = true;
                    if (!vm.SelectedRows.Contains(r))
                        vm.SelectedRows.Add(r);
                }
        }

    }
}
