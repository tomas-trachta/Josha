using Josha.Business;
using Josha.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Data;

namespace Josha.ViewModels
{
    internal enum ListSortColumn { Name, Extension, Size, Modified }

    internal class FileListViewModel : BaseViewModel
    {
        private string _currentPath = "";
        private string _filterText = "";
        private bool _showHiddenFiles;
        private bool _isLoading;
        private string? _loadError;
        private ListSortColumn _sortColumn = ListSortColumn.Name;
        private bool _sortAscending = true;
        private Predicate<object>? _filterPredicate;
        private CancellationTokenSource? _refreshCts;

        public ObservableCollection<FileRowViewModel> Rows { get; } = new();
        public ICollectionView RowsView { get; }

        // Kept in sync by FileListView with the ListView's native SelectedItems.
        public ObservableCollection<FileRowViewModel> SelectedRows { get; } = new();

        // Active filesystem provider. Defaults to local; remote tabs swap in
        // their RemoteFileSystemProvider so enumeration goes over the wire.
        public IFileSystemProvider FileSystem { get; set; } = LocalFileSystemProvider.Instance;

        public string CurrentPath
        {
            get => _currentPath;
            private set
            {
                if (_currentPath == value) return;
                _currentPath = value;
                OnPropertyChanged();
            }
        }

        public string FilterText
        {
            get => _filterText;
            set
            {
                if (_filterText == value) return;
                _filterText = value;
                OnPropertyChanged();
                UpdateFilterPredicate();
                RowsView.Refresh();
            }
        }

        public bool ShowHiddenFiles
        {
            get => _showHiddenFiles;
            set
            {
                if (_showHiddenFiles == value) return;
                _showHiddenFiles = value;
                OnPropertyChanged();
                _ = RefreshAsync();
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set { if (_isLoading == value) return; _isLoading = value; OnPropertyChanged(); }
        }

        public string? LoadError
        {
            get => _loadError;
            private set { if (_loadError == value) return; _loadError = value; OnPropertyChanged(); }
        }

        public ListSortColumn SortColumn => _sortColumn;
        public bool SortAscending => _sortAscending;

        public FileListViewModel()
        {
            RowsView = CollectionViewSource.GetDefaultView(Rows);
            RebuildSortDescriptions();
            RowsView.Filter = obj => _filterPredicate?.Invoke(obj) ?? true;
        }

        // Same column → flip direction; different column → switch and reset to ascending.
        public void SetSort(ListSortColumn column)
        {
            if (column == _sortColumn)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _sortColumn = column;
                _sortAscending = true;
                OnPropertyChanged(nameof(SortColumn));
            }
            OnPropertyChanged(nameof(SortAscending));
            RebuildSortDescriptions();
        }

        public async Task NavigateAsync(string path, CancellationToken ct = default)
        {
            // A filter scoped to the previous directory's names rarely makes
            // sense in the new one — clear it on every actual directory change.
            if (!string.Equals(_currentPath, path, StringComparison.OrdinalIgnoreCase))
                FilterText = "";
            CurrentPath = path;
            await RefreshAsync(ct);
        }

        public async Task RefreshAsync(CancellationToken ct = default)
        {
            var path = _currentPath;
            if (string.IsNullOrEmpty(path))
            {
                Rows.Clear();
                return;
            }

            // Cancel any in-flight refresh so only the latest navigation wins.
            _refreshCts?.Cancel();
            _refreshCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _refreshCts.Token;

            var showHidden = _showHiddenFiles;
            IsLoading = true;
            LoadError = null;

            var fs = FileSystem;
            List<FileRowViewModel> newRows;
            try
            {
                if (fs.IsRemote)
                    newRows = await EnumerateRemoteAsync(fs, path, token).ConfigureAwait(true);
                else
                    newRows = await Task.Run(() => EnumerateRows(path, showHidden, token), token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                IsLoading = false;
                return;
            }
            catch (Exception ex)
            {
                Log.Error("FileList", $"Enumerate failed for {path}", ex);
                LoadError = ex.Message;
                Rows.Clear();
                IsLoading = false;
                return;
            }

            if (token.IsCancellationRequested) { IsLoading = false; return; }

            Rows.Clear();
            foreach (var r in newRows)
                Rows.Add(r);
            IsLoading = false;
        }

        private static List<FileRowViewModel> EnumerateRows(string path, bool showHidden, CancellationToken ct)
        {
            var rows = new List<FileRowViewModel>();
            DirectoryInfo di;
            try { di = new DirectoryInfo(path); }
            catch { return rows; }

            var parent = di.Parent?.FullName;
            if (!string.IsNullOrEmpty(parent))
                rows.Add(FileRowViewModel.ParentLink(parent));

            DirectoryInfo[] subDirs;
            FileInfo[] files;
            try
            {
                subDirs = di.GetDirectories();
                files = di.GetFiles();
            }
            catch
            {
                return rows;
            }

            foreach (var sub in subDirs)
            {
                ct.ThrowIfCancellationRequested();
                if (!showHidden && IsHidden(sub.Attributes)) continue;
                rows.Add(FileRowViewModel.FromDirectory(sub));
            }

            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                if (!showHidden && IsHidden(f.Attributes)) continue;
                rows.Add(FileRowViewModel.FromFile(f));
            }

            return rows;
        }

        private static bool IsHidden(FileAttributes attrs) =>
            (attrs & FileAttributes.Hidden) == FileAttributes.Hidden;

        private static async Task<List<FileRowViewModel>> EnumerateRemoteAsync(
            IFileSystemProvider fs, string path, CancellationToken ct)
        {
            var rows = new List<FileRowViewModel>();
            var parent = RemoteParent(path);
            if (parent != null) rows.Add(FileRowViewModel.ParentLink(parent));

            var entries = await fs.EnumerateAsync(path, ct).ConfigureAwait(false);
            foreach (var e in entries)
            {
                ct.ThrowIfCancellationRequested();
                rows.Add(FileRowViewModel.FromEntry(e));
            }
            return rows;
        }

        private static string? RemoteParent(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "/") return null;
            var trimmed = path.TrimEnd('/');
            var i = trimmed.LastIndexOf('/');
            if (i <= 0) return "/";
            return trimmed.Substring(0, i);
        }

        // Three tiers: ".." pinned at top, then directories, then user's choice.
        private void RebuildSortDescriptions()
        {
            RowsView.SortDescriptions.Clear();
            RowsView.SortDescriptions.Add(new SortDescription(
                nameof(FileRowViewModel.IsParentLink), ListSortDirection.Descending));
            RowsView.SortDescriptions.Add(new SortDescription(
                nameof(FileRowViewModel.IsDirectory), ListSortDirection.Descending));

            var dir = _sortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending;
            var prop = _sortColumn switch
            {
                ListSortColumn.Name      => nameof(FileRowViewModel.Name),
                ListSortColumn.Extension => nameof(FileRowViewModel.Extension),
                ListSortColumn.Size      => nameof(FileRowViewModel.SizeBytes),
                ListSortColumn.Modified  => nameof(FileRowViewModel.LastModifiedUtc),
                _                        => nameof(FileRowViewModel.Name),
            };
            RowsView.SortDescriptions.Add(new SortDescription(prop, dir));
        }

        private void UpdateFilterPredicate()
        {
            if (string.IsNullOrEmpty(_filterText))
            {
                _filterPredicate = null;
                return;
            }

            var text = _filterText;
            if (text.IndexOfAny(['*', '?']) >= 0)
            {
                var pattern = "^" + Regex.Escape(text)
                    .Replace("\\*", ".*")
                    .Replace("\\?", ".") + "$";
                Regex rx;
                try { rx = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled); }
                catch { _filterPredicate = _ => true; return; }

                _filterPredicate = obj =>
                {
                    if (obj is not FileRowViewModel row) return true;
                    return row.IsParentLink || rx.IsMatch(row.Name);
                };
            }
            else
            {
                _filterPredicate = obj =>
                {
                    if (obj is not FileRowViewModel row) return true;
                    return row.IsParentLink ||
                           row.Name.Contains(text, StringComparison.OrdinalIgnoreCase);
                };
            }
        }

        public void StartRename(FileRowViewModel row)
        {
            if (row == null || row.IsParentLink) return;
            foreach (var r in Rows)
                if (r != row && r.IsEditing) r.IsEditing = false;
            row.IsEditing = true;
        }

        public void CancelRename(FileRowViewModel row)
        {
            if (row != null) row.IsEditing = false;
        }

        public async Task CommitRenameAsync(FileRowViewModel row, string newName)
        {
            if (row == null) return;
            if (string.IsNullOrWhiteSpace(newName) || newName == row.Name)
            {
                row.IsEditing = false;
                return;
            }

            var result = await FileSystem.RenameAsync(row.FullPath, newName).ConfigureAwait(true);
            row.IsEditing = false;

            if (!result.Success)
            {
                Log.Warn("FileList", $"Rename failed for '{row.Name}' → '{newName}': {result.Error}");
                AppServices.Toast.Error($"Rename failed: {result.Error}");
                return;
            }

            AppServices.Toast.Success($"Renamed to '{newName}'");
            await RefreshAsync();
        }
    }
}
