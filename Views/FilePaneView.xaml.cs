using Josha.Business;
using Josha.Services;
using Josha.ViewModels;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Josha.Views
{
    public partial class FilePaneView : UserControl
    {
        internal event Action<FilePaneViewModel>? Activated;

        internal void FocusFilterBox() => ListViewControl.FocusFilterBox();
        internal void FocusList()      => ListViewControl.FocusList();

        private async void OnReconnectClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not FilePaneViewModel vm || !vm.IsRemote || vm.Site == null) return;
            await vm.DisconnectAsync();
            await vm.ConnectAsync();
        }

        public FilePaneView()
        {
            InitializeComponent();

            ListViewControl.RowActivated += OnRowActivated;
            ListViewControl.NavigateUpRequested += OnNavigateUp;
            TilesViewControl.RowActivated += OnRowActivated;
            TilesViewControl.NavigateUpRequested += OnNavigateUp;

            DiskUsageTree.SelectionChanged += OnDiskUsageSelectionChanged;

            DataContextChanged += OnDataContextChanged;
            PreviewMouseDown += OnPreviewMouseDown;
        }

        private void OnDiskUsageSelectionChanged()
        {
            if (DataContext is not FilePaneViewModel vm) return;
            vm.DiskUsageSelection.Clear();
            foreach (var n in DiskUsageTree.SelectedNodes)
                vm.DiskUsageSelection.Add(n);
            CommandManager.InvalidateRequerySuggested();
        }

        private static readonly GridLength PreviewColumnVisibleWidth = new(280);
        private static readonly GridLength PreviewColumnHiddenWidth = new(0);

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is FilePaneViewModel oldVm)
                oldVm.PropertyChanged -= OnVmPropertyChanged;

            if (e.NewValue is FilePaneViewModel newVm)
            {
                newVm.PropertyChanged += OnVmPropertyChanged;
                ApplyActiveBorder(newVm.IsActive);
                ApplyPreviewColumnWidth(newVm.IsPreviewPaneVisible);
            }
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not FilePaneViewModel vm) return;

            if (e.PropertyName == nameof(FilePaneViewModel.IsActive))
                ApplyActiveBorder(vm.IsActive);
            else if (e.PropertyName == nameof(FilePaneViewModel.IsPreviewPaneVisible))
                ApplyPreviewColumnWidth(vm.IsPreviewPaneVisible);
        }

        // Visibility=Collapsed on QuickPreviewPane doesn't shrink the Grid
        // column, so set the column width directly.
        private void ApplyPreviewColumnWidth(bool isVisible)
        {
            PreviewColumn.Width = isVisible ? PreviewColumnVisibleWidth : PreviewColumnHiddenWidth;
        }

        private bool _isDragOver;

        private void ApplyActiveBorder(bool isActive)
        {
            var key = (isActive || _isDragOver) ? "Brush.TreeBorderActive" : "Brush.TreeBorderInactive";
            if (Application.Current?.Resources[key] is Brush brush)
                ActiveBorder.BorderBrush = brush;
        }

        private void RefreshActiveBorder()
        {
            var isActive = (DataContext as FilePaneViewModel)?.IsActive ?? false;
            ApplyActiveBorder(isActive);
        }

        private void OnRowActivated(FileRowViewModel row)
        {
            if (DataContext is not FilePaneViewModel vm) return;

            if (row.IsDirectory)
            {
                _ = vm.NavigateAsync(row.FullPath);
                return;
            }

            if (vm.IsRemote)
            {
                _ = OpenRemoteFileAsync(vm, row);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = row.FullPath,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Log.Warn("Pane", $"Open default failed for {row.FullPath}", ex);
                AppServices.Toast.Error($"Couldn't open {row.Name}: {ex.Message}");
            }
        }

        // Remote files have no local path. Download to %TEMP%\Josha\opens
        // and shell-open the temp. View-only — saves don't go back to the server
        // (use F4 for that, which routes through EditOnServerWatcher).
        private static async Task OpenRemoteFileAsync(FilePaneViewModel vm, FileRowViewModel row)
        {
            string? tempPath = null;
            try
            {
                AppServices.Toast.Info($"Downloading {row.Name}…");

                var dir = Path.Combine(Path.GetTempPath(), "Josha", "opens", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                tempPath = Path.Combine(dir, row.Name);

                await using (var src = await vm.FileSystem.OpenReadAsync(row.FullPath).ConfigureAwait(true))
                await using (var dst = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    256 * 1024, useAsync: true))
                {
                    await src.CopyToAsync(dst).ConfigureAwait(true);
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = tempPath,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Log.Warn("Pane", $"Open remote failed for {row.FullPath}", ex);
                AppServices.Toast.Error($"Couldn't open {row.Name}: {ex.Message}");
                if (tempPath != null) { try { File.Delete(tempPath); } catch { } }
            }
        }

        private void OnNavigateUp()
        {
            if (DataContext is not FilePaneViewModel vm) return;
            if (vm.NavigateUpCommand.CanExecute(null))
                vm.NavigateUpCommand.Execute(null);
        }

        private void OnDiskUsageSearchBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox tb) return;
            if (DataContext is not FilePaneViewModel vm) return;

            var resultsList = FindResultsListFor(tb);
            var resultsCount = resultsList?.Items.Count ?? 0;

            switch (e.Key)
            {
                case Key.Down:
                    if (resultsList != null && resultsCount > 0)
                    {
                        resultsList.SelectedIndex = 0;
                        resultsList.Focus();
                        if (resultsList.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem container)
                            container.Focus();
                    }
                    e.Handled = true;
                    break;
                case Key.Enter:
                    if (resultsList != null && resultsList.Items.Count > 0
                        && resultsList.Items[0] is Models.SearchResult hit)
                    {
                        ActivateDiskUsageSearchResult(hit);
                        e.Handled = true;
                    }
                    break;
                case Key.Escape:
                    vm.ClearAllSearches();
                    e.Handled = true;
                    break;
            }
        }

        private void OnDiskUsageSearchResultsKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not ListBox list) return;
            if (e.Key == Key.Enter && list.SelectedItem is Models.SearchResult hit)
            {
                ActivateDiskUsageSearchResult(hit);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (DataContext is FilePaneViewModel vm) vm.ClearAllSearches();
                FindBoxFor(list)?.Focus();
                e.Handled = true;
            }
        }

        private void OnDiskUsageSearchResultActivated(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListBox list) return;
            var src = e.OriginalSource as DependencyObject;
            var container = ItemsControl.ContainerFromElement(list, src) as ListBoxItem;
            if (container?.Content is Models.SearchResult hit)
            {
                ActivateDiskUsageSearchResult(hit);
                e.Handled = true;
            }
        }

        private void ActivateDiskUsageSearchResult(Models.SearchResult hit)
        {
            DiskUsageTree.NavigateTo(hit);
            if (DataContext is FilePaneViewModel vm) vm.ClearAllSearches();
        }

        private ListBox? FindResultsListFor(TextBox tb)
        {
            if (string.IsNullOrEmpty(tb.Name) || !tb.Name.EndsWith("Box")) return null;
            var listName = tb.Name[..^"Box".Length] + "ResultsList";
            return FindName(listName) as ListBox;
        }

        private TextBox? FindBoxFor(ListBox list)
        {
            if (string.IsNullOrEmpty(list.Name) || !list.Name.EndsWith("ResultsList")) return null;
            var boxName = list.Name[..^"ResultsList".Length] + "Box";
            return FindName(boxName) as TextBox;
        }

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not FilePaneViewModel vm) return;

            Activated?.Invoke(vm);

            if (e.ChangedButton == MouseButton.XButton1 && vm.BackCommand.CanExecute(null))
            {
                vm.BackCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.ChangedButton == MouseButton.XButton2 && vm.ForwardCommand.CanExecute(null))
            {
                vm.ForwardCommand.Execute(null);
                e.Handled = true;
            }
        }

        // ─────────────────────────────────────────────── Drop target ─────────

        private static DragDropEffects EffectFor(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return DragDropEffects.None;
            // Ctrl forces copy; Shift forces move; default = copy.
            if ((e.KeyStates & DragDropKeyStates.ShiftKey) == DragDropKeyStates.ShiftKey)
                return DragDropEffects.Move;
            return DragDropEffects.Copy;
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effects = EffectFor(e);
            if (e.Effects != DragDropEffects.None && !_isDragOver)
            {
                _isDragOver = true;
                RefreshActiveBorder();
            }
            e.Handled = true;
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = EffectFor(e);
            if (e.Effects != DragDropEffects.None && !_isDragOver)
            {
                _isDragOver = true;
                RefreshActiveBorder();
            }
            e.Handled = true;
        }

        private void OnDragLeave(object sender, DragEventArgs e)
        {
            // DragLeave fires whenever the cursor enters a child element too,
            // so guard against false positives by checking the cursor really
            // left the pane bounds before dropping the highlight.
            var pos = e.GetPosition(ActiveBorder);
            var bounds = new Rect(0, 0, ActiveBorder.ActualWidth, ActiveBorder.ActualHeight);
            if (bounds.Contains(pos)) return;

            if (_isDragOver)
            {
                _isDragOver = false;
                RefreshActiveBorder();
            }
            e.Handled = true;
        }

        private async void OnDrop(object sender, DragEventArgs e)
        {
            if (_isDragOver)
            {
                _isDragOver = false;
                RefreshActiveBorder();
            }

            if (DataContext is not FilePaneViewModel vm) return;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null || paths.Length == 0) return;

            var dst = vm.CurrentPath;
            if (string.IsNullOrEmpty(dst)) return;

            var isMove = (e.KeyStates & DragDropKeyStates.ShiftKey) == DragDropKeyStates.ShiftKey;
            e.Handled = true;

            int ok = 0, fail = 0;
            string? lastError = null;
            foreach (var src in paths)
            {
                if (string.IsNullOrEmpty(src)) continue;

                // Drop onto same dir → no-op
                var srcDir = Path.GetDirectoryName(src.TrimEnd('\\', '/'));
                if (string.Equals(srcDir, dst, StringComparison.OrdinalIgnoreCase)) continue;

                var dstPath = Path.Combine(dst, Path.GetFileName(src.TrimEnd('\\', '/')));
                var result = isMove
                    ? await FileOpsComponent.MoveAsync(src, dstPath)
                    : await FileOpsComponent.CopyAsync(src, dstPath);

                if (result.Success) ok++;
                else { fail++; lastError = result.Error; }
            }

            await vm.List.RefreshAsync();

            if (ok + fail > 0)
            {
                var verb = isMove ? "Moved" : "Copied";
                if (fail == 0) AppServices.Toast.Success($"{verb}: {ok} item{(ok == 1 ? "" : "s")}");
                else if (ok == 0) AppServices.Toast.Error($"{verb} failed: {lastError ?? "see log"}");
                else AppServices.Toast.Warning($"{verb}: {ok} ok, {fail} failed");
            }
        }
    }
}
