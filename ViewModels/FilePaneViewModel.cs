using Josha.Business;
using Josha.Models;
using Josha.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Input;
using System.Windows.Threading;

namespace Josha.ViewModels
{
    internal class FilePaneViewModel : BaseViewModel
    {
        private string _currentPath = "";
        private ViewMode _currentMode = ParseDefaultViewMode(AppServices.Settings.DefaultViewMode);
        private bool _isActive;
        private DirectoryTreeItemViewModel? _diskUsageRoot;
        private bool _isScanningDrive;
        private bool _isLoadingDiskUsage;
        private string _freeSpaceStatus = "";

        private bool _isTreeScrollMode;
        private string _allSearchQuery = "";
        private string _filesSearchQuery = "";
        private string _foldersSearchQuery = "";
        private DispatcherTimer? _allSearchTimer;
        private DispatcherTimer? _filesSearchTimer;
        private DispatcherTimer? _foldersSearchTimer;

        public FileListViewModel List { get; }

        // Active filesystem provider for this tab. Local panes share
        // LocalFileSystemProvider.Instance; remote tabs swap in their own.
        public IFileSystemProvider FileSystem
        {
            get => List.FileSystem;
            set
            {
                if (List.FileSystem == value) return;
                List.FileSystem = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsRemote));
            }
        }

        public bool IsRemote => FileSystem.IsRemote;

        private bool _isPreviewPaneVisible;
        public bool IsPreviewPaneVisible
        {
            get => _isPreviewPaneVisible;
            set
            {
                if (_isPreviewPaneVisible == value) return;
                _isPreviewPaneVisible = value;
                OnPropertyChanged();
                if (value) _ = RefreshPreviewAsync();
            }
        }

        private FilePreviewViewModel? _currentPreview;
        public FilePreviewViewModel? CurrentPreview
        {
            get => _currentPreview;
            private set
            {
                if (_currentPreview == value) return;
                _currentPreview = value;
                OnPropertyChanged();
            }
        }

        // Recomputes the preview for whatever's focused in the active list. The
        // preview pane (when visible) binds to CurrentPreview; this is also
        // called from the list's SelectionChanged so navigating with arrows
        // updates the preview live.
        public async Task RefreshPreviewAsync()
        {
            if (!_isPreviewPaneVisible) return;
            var focused = List.SelectedRows.FirstOrDefault(r => !r.IsParentLink);
            if (focused == null) { CurrentPreview = null; return; }
            CurrentPreview = await FilePreviewViewModel.LoadAsync(FileSystem, focused);
        }

        public Models.FtpSite? Site { get; private set; }

        private Models.ConnectionStatus _connectionStatus = Models.ConnectionStatus.Disconnected;
        public Models.ConnectionStatus ConnectionStatus
        {
            get => _connectionStatus;
            private set
            {
                if (_connectionStatus == value) return;
                _connectionStatus = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ConnectionStatusBrushKey));
            }
        }

        public string ConnectionStatusBrushKey => _connectionStatus switch
        {
            Models.ConnectionStatus.Connected    => "Brush.Status.Ok",
            Models.ConnectionStatus.Connecting   => "Brush.Status.Pending",
            Models.ConnectionStatus.Reconnecting => "Brush.Status.Pending",
            Models.ConnectionStatus.Error        => "Brush.Status.Error",
            _                                    => "Brush.OnSurfaceMuted",
        };

        public string CurrentPath
        {
            get => _currentPath;
            private set
            {
                if (_currentPath == value) return;
                _currentPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DriveLetter));
                OnPropertyChanged(nameof(TabTitle));
                _ = RefreshFreeSpaceAsync();
                if (_currentMode == ViewMode.DiskUsage)
                    _ = LoadDiskUsageRootAsync();
            }
        }

        public string TabTitle
        {
            get
            {
                if (string.IsNullOrEmpty(_currentPath)) return "(empty)";
                try
                {
                    var trimmed = _currentPath.TrimEnd('\\', '/');
                    var leaf = Path.GetFileName(trimmed);
                    if (!string.IsNullOrEmpty(leaf)) return leaf;
                    var root = Path.GetPathRoot(_currentPath);
                    return root?.TrimEnd('\\') ?? _currentPath;
                }
                catch { return _currentPath; }
            }
        }

        public string DriveLetter
        {
            get
            {
                try
                {
                    var root = Path.GetPathRoot(_currentPath);
                    if (string.IsNullOrEmpty(root)) return "";
                    return root.TrimEnd('\\', '/').TrimEnd(':').ToUpperInvariant();
                }
                catch { return ""; }
            }
        }

        public ViewMode CurrentMode
        {
            get => _currentMode;
            set
            {
                if (_currentMode == value) return;
                _currentMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsListMode));
                OnPropertyChanged(nameof(IsTilesMode));
                OnPropertyChanged(nameof(IsDiskUsageMode));
                OnPropertyChanged(nameof(NeedsScan));
                if (value == ViewMode.DiskUsage)
                    _ = LoadDiskUsageRootAsync();
            }
        }

        public bool IsListMode      => _currentMode == ViewMode.List;
        public bool IsTilesMode     => _currentMode == ViewMode.Tiles;
        public bool IsDiskUsageMode => _currentMode == ViewMode.DiskUsage;

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value) return;
                _isActive = value;
                OnPropertyChanged();
            }
        }

        // Distinct from IsActive (which is F-key focus across the whole app):
        // this is set on the column's currently-shown tab, regardless of whether
        // the column itself is the focused one. Drives the tab-strip highlight.
        private bool _isCurrentInColumn;
        public bool IsCurrentInColumn
        {
            get => _isCurrentInColumn;
            set
            {
                if (_isCurrentInColumn == value) return;
                _isCurrentInColumn = value;
                OnPropertyChanged();
            }
        }

        public DirectoryTreeItemViewModel? DiskUsageRoot
        {
            get => _diskUsageRoot;
            private set
            {
                if (_diskUsageRoot == value) return;
                _diskUsageRoot = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasDiskUsageRoot));
                OnPropertyChanged(nameof(NeedsScan));
            }
        }

        public bool HasDiskUsageRoot => _diskUsageRoot != null;

        public List<TreeItemViewModel> DiskUsageSelection { get; } = new();

        public bool IsTreeScrollMode
        {
            get => _isTreeScrollMode;
            set { if (_isTreeScrollMode == value) return; _isTreeScrollMode = value; OnPropertyChanged(); }
        }

        public string AllSearchQuery
        {
            get => _allSearchQuery;
            set
            {
                if (_allSearchQuery == value) return;
                _allSearchQuery = value ?? "";
                OnPropertyChanged();
                ScheduleSearch(ref _allSearchTimer, () => RunSearch(_allSearchQuery, AllSearchResults, scope: null,
                    nameof(HasAllSearchResults)));
            }
        }

        public string FilesSearchQuery
        {
            get => _filesSearchQuery;
            set
            {
                if (_filesSearchQuery == value) return;
                _filesSearchQuery = value ?? "";
                OnPropertyChanged();
                ScheduleSearch(ref _filesSearchTimer, () => RunSearch(_filesSearchQuery, FilesSearchResults,
                    SearchResultType.File, nameof(HasFilesSearchResults)));
            }
        }

        public string FoldersSearchQuery
        {
            get => _foldersSearchQuery;
            set
            {
                if (_foldersSearchQuery == value) return;
                _foldersSearchQuery = value ?? "";
                OnPropertyChanged();
                ScheduleSearch(ref _foldersSearchTimer, () => RunSearch(_foldersSearchQuery, FoldersSearchResults,
                    SearchResultType.Directory, nameof(HasFoldersSearchResults)));
            }
        }

        public ObservableCollection<SearchResult> AllSearchResults     { get; } = new();
        public ObservableCollection<SearchResult> FilesSearchResults   { get; } = new();
        public ObservableCollection<SearchResult> FoldersSearchResults { get; } = new();

        public bool HasAllSearchResults     => AllSearchResults.Count > 0;
        public bool HasFilesSearchResults   => FilesSearchResults.Count > 0;
        public bool HasFoldersSearchResults => FoldersSearchResults.Count > 0;

        // 250ms debounce: a full C:\ walk hits ~50k dirs.
        private void ScheduleSearch(ref DispatcherTimer? timer, Action run)
        {
            if (timer == null)
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                t.Tick += (_, _) => { t.Stop(); run(); };
                timer = t;
            }
            timer.Stop();
            timer.Start();
        }

        private void RunSearch(string query, ObservableCollection<SearchResult> sink,
                               SearchResultType? scope, string hasResultsProp)
        {
            sink.Clear();
            query = (query ?? "").Trim();
            if (query.Length == 0)
            {
                OnPropertyChanged(hasResultsProp);
                return;
            }

            var root = _diskUsageRoot?.Model;
            if (root == null)
            {
                OnPropertyChanged(hasResultsProp);
                return;
            }

            var search = new SearchComponent();
            try { search.Search(query, root); }
            catch (Exception ex) { Log.Warn("Pane", $"Disk-usage search failed for '{query}'", ex); }

            foreach (var hit in search.SearchResults)
            {
                if (scope.HasValue && hit.ResultType != scope.Value) continue;
                sink.Add(hit);
            }

            OnPropertyChanged(hasResultsProp);
        }

        public void ClearAllSearches()
        {
            _allSearchTimer?.Stop();
            _filesSearchTimer?.Stop();
            _foldersSearchTimer?.Stop();
            AllSearchResults.Clear();
            FilesSearchResults.Clear();
            FoldersSearchResults.Clear();
            OnPropertyChanged(nameof(HasAllSearchResults));
            OnPropertyChanged(nameof(HasFilesSearchResults));
            OnPropertyChanged(nameof(HasFoldersSearchResults));
            AllSearchQuery = "";
            FilesSearchQuery = "";
            FoldersSearchQuery = "";
        }

        public bool IsScanningDrive
        {
            get => _isScanningDrive;
            private set
            {
                if (_isScanningDrive == value) return;
                _isScanningDrive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NeedsScan));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsLoadingDiskUsage
        {
            get => _isLoadingDiskUsage;
            private set
            {
                if (_isLoadingDiskUsage == value) return;
                _isLoadingDiskUsage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NeedsScan));
            }
        }

        // True only when Disk Usage really has no snapshot to fall back on —
        // not while one is mid-load from disk (which can take a couple of seconds
        // on a cold cache for a large tree.daps).
        public bool NeedsScan =>
            _currentMode == ViewMode.DiskUsage
            && _diskUsageRoot == null
            && !_isScanningDrive
            && !_isLoadingDiskUsage
            && !string.IsNullOrEmpty(DriveLetter);

        public ICommand SwitchToListCommand { get; }
        public ICommand SwitchToTilesCommand { get; }
        public ICommand SwitchToDiskUsageCommand { get; }
        public ICommand NavigateUpCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ScanCurrentDriveCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand ForwardCommand { get; }

        private readonly List<string> _backHistory = new();
        private readonly List<string> _forwardHistory = new();

        public bool CanGoBack => _backHistory.Count > 0;
        public bool CanGoForward => _forwardHistory.Count > 0;

        public string SelectionStatus
        {
            get
            {
                int folders = 0, files = 0;
                long folderBytes = 0, fileBytes = 0;
                foreach (var r in List.Rows)
                {
                    if (r.IsParentLink) continue;
                    if (r.IsDirectory)
                    {
                        folders++;
                        if (r.SizeBytes is long fb) folderBytes += fb;
                    }
                    else
                    {
                        files++;
                        if (r.SizeBytes is long b) fileBytes += b;
                    }
                }

                int total = folders + files;

                int selected = 0;
                long selectedBytes = 0;
                foreach (var r in List.SelectedRows)
                {
                    if (r.IsParentLink) continue;
                    selected++;
                    if (r.SizeBytes is long b) selectedBytes += b;
                }

                if (selected > 0)
                    return $"{selected:N0} of {total:N0} selected · {FormatBytes(selectedBytes)}";

                if (!string.IsNullOrEmpty(List.FilterText) && List.RowsView is System.Windows.Data.CollectionView cv)
                {
                    int matched = cv.Count;
                    foreach (var item in List.RowsView)
                        if (item is FileRowViewModel rr && rr.IsParentLink) { matched--; break; }
                    if (matched < 0) matched = 0;
                    return $"Filtered {matched:N0} of {total:N0} · '{List.FilterText}'";
                }

                long totalBytes = folderBytes + fileBytes;
                return $"{folders:N0} folder{(folders == 1 ? "" : "s")}, {files:N0} file{(files == 1 ? "" : "s")} · {FormatBytes(totalBytes)}";
            }
        }

        public string SortStatus
        {
            get
            {
                var arrow = List.SortAscending ? "↑" : "↓";
                var name = List.SortColumn switch
                {
                    ListSortColumn.Name      => "Name",
                    ListSortColumn.Extension => "Ext",
                    ListSortColumn.Size      => "Size",
                    ListSortColumn.Modified  => "Modified",
                    _ => List.SortColumn.ToString()
                };
                return $"{arrow} {name}";
            }
        }

        // Backed by an async refresh — DriveInfo property access can block on
        // flaky removable / network drives, so a sync getter would freeze the
        // dispatcher every time bindings re-evaluate.
        public string FreeSpaceStatus
        {
            get => _freeSpaceStatus;
            private set { if (_freeSpaceStatus == value) return; _freeSpaceStatus = value; OnPropertyChanged(); }
        }

        private async Task RefreshFreeSpaceAsync()
        {
            var letter = DriveLetter;
            if (string.IsNullOrEmpty(letter)) { FreeSpaceStatus = ""; return; }

            string status;
            try
            {
                status = await Task.Run(() =>
                {
                    try
                    {
                        var di = new DriveInfo(letter);
                        if (!di.IsReady) return "";
                        return $"Free {FormatBytes(di.AvailableFreeSpace)} of {FormatBytes(di.TotalSize)} on {letter}:";
                    }
                    catch { return ""; }
                }).ConfigureAwait(true);
            }
            catch { status = ""; }

            FreeSpaceStatus = status;
        }

        private static ViewMode ParseDefaultViewMode(string? raw) =>
            raw switch
            {
                "Tiles" => ViewMode.Tiles,
                "DiskUsage" => ViewMode.DiskUsage,
                _ => ViewMode.List,
            };

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes:N0} B";
            double v = bytes / 1024.0;
            if (v < 1024) return $"{v:N1} KB";
            v /= 1024;
            if (v < 1024) return $"{v:N1} MB";
            v /= 1024;
            if (v < 1024) return $"{v:N2} GB";
            v /= 1024;
            return $"{v:N2} TB";
        }

        public FilePaneViewModel() : this(initialPath: null) { }

        public FilePaneViewModel(string? initialPath = @"C:\")
        {
            List = new FileListViewModel();

            NotifyCollectionChangedEventHandler bumpSelectionStatus =
                (_, _) => OnPropertyChanged(nameof(SelectionStatus));
            ((INotifyCollectionChanged)List.Rows).CollectionChanged += bumpSelectionStatus;
            ((INotifyCollectionChanged)List.SelectedRows).CollectionChanged += bumpSelectionStatus;

            // Filter changes don't touch Rows but the visible-count summary does.
            List.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FileListViewModel.FilterText))
                    OnPropertyChanged(nameof(SelectionStatus));
                else if (e.PropertyName == nameof(FileListViewModel.SortColumn)
                      || e.PropertyName == nameof(FileListViewModel.SortAscending))
                    OnPropertyChanged(nameof(SortStatus));
            };
            ((INotifyCollectionChanged)List.SelectedRows).CollectionChanged += (_, _) =>
            {
                if (_isPreviewPaneVisible) _ = RefreshPreviewAsync();
            };

            SwitchToListCommand = new RelayCommand(_ => CurrentMode = ViewMode.List);
            SwitchToTilesCommand = new RelayCommand(_ => CurrentMode = ViewMode.Tiles);
            SwitchToDiskUsageCommand = new RelayCommand(_ => CurrentMode = ViewMode.DiskUsage);
            NavigateUpCommand = new RelayCommand(_ => _ = NavigateUpAsync(), _ => CanNavigateUp());
            RefreshCommand = new RelayCommand(_ => _ = List.RefreshAsync());
            ScanCurrentDriveCommand = new RelayCommand(
                _ => _ = ScanCurrentDriveAsync(),
                _ => !_isScanningDrive && !string.IsNullOrEmpty(DriveLetter));

            BackCommand    = new RelayCommand(_ => _ = GoBackAsync(),    _ => CanGoBack);
            ForwardCommand = new RelayCommand(_ => _ = GoForwardAsync(), _ => CanGoForward);

            if (!string.IsNullOrEmpty(initialPath))
                _ = NavigateAsync(initialPath);
        }

        private async Task LoadDiskUsageRootAsync()
        {
            var path = _currentPath;
            var drive = DriveLetter;
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(drive))
            {
                DiskUsageRoot = null;
                return;
            }

            IsLoadingDiskUsage = true;
            try
            {
                DirOD? driveRoot;
                try { driveRoot = await AppServices.Snapshot.LoadAsync(drive).ConfigureAwait(true); }
                catch (Exception ex)
                {
                    Log.Warn("Pane", $"Snapshot load failed for drive {drive}", ex);
                    DiskUsageRoot = null;
                    return;
                }

                if (driveRoot == null)
                {
                    DiskUsageRoot = null;
                    return;
                }

                var subtree = driveRoot.FindByPath(path);
                if (subtree == null)
                {
                    DiskUsageRoot = null;
                    return;
                }

                DiskUsageRoot = new DirectoryTreeItemViewModel(subtree, depth: 0);
            }
            finally
            {
                IsLoadingDiskUsage = false;
            }
        }

        private async Task ScanCurrentDriveAsync()
        {
            var drive = DriveLetter;
            if (string.IsNullOrEmpty(drive)) return;

            IsScanningDrive = true;
            try
            {
                await AppServices.Snapshot.ScanDriveAsync(drive).ConfigureAwait(true);
                await LoadDiskUsageRootAsync();
            }
            catch (Exception ex)
            {
                Log.Error("Pane", $"Scan-current-drive failed for {drive}", ex);
            }
            finally
            {
                IsScanningDrive = false;
            }
        }

        public Task NavigateAsync(string path) => NavigateInternalAsync(path, fromHistory: false);

        private async Task NavigateInternalAsync(string path, bool fromHistory)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            string full;
            if (IsRemote)
            {
                full = path;
            }
            else
            {
                var promoted = PromoteHostToUnc(path);
                try { full = Path.GetFullPath(promoted); }
                catch (Exception ex)
                {
                    Log.Warn("Pane", $"Could not normalize path '{path}'", ex);
                    return;
                }
            }

            if (string.Equals(full, _currentPath, StringComparison.OrdinalIgnoreCase))
                return;

            if (!fromHistory)
            {
                if (!string.IsNullOrEmpty(_currentPath))
                    _backHistory.Add(_currentPath);
                _forwardHistory.Clear();
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(CanGoForward));
                CommandManager.InvalidateRequerySuggested();
            }

            CurrentPath = full;

            try
            {
                await List.NavigateAsync(full);
                Log.Info("Pane", $"Navigated to {full}");
            }
            catch (Exception ex)
            {
                Log.Warn("Pane", $"NavigateAsync inner failure for {full}", ex);
            }
        }

        // Bare IP / FQDN typed into the path bar gets promoted to a UNC root
        // (\\host or \\host\share\...). Windows' SMB redirector then handles
        // name resolution, port-445 connect, and auth — same UX as Explorer
        // or OneCommander. Already-rooted paths and scheme-prefixed inputs
        // (ftp://, sftp://, ...) are left untouched for the remote-site path.
        private static string PromoteHostToUnc(string input)
        {
            var s = input.Trim();
            if (s.Length < 2) return input;

            if (s.StartsWith(@"\\") || s.StartsWith("//")) return input;
            if (s[1] == ':') return input;
            if (s.Contains("://", StringComparison.Ordinal)) return input;

            int sep = s.IndexOfAny(['\\', '/']);
            var host = sep < 0 ? s : s[..sep];

            // IPv4/IPv6 literal, or anything containing a '.' that isn't a
            // relative-path marker — treat as a host. Single-label words
            // ("Documents") stay as relative paths so local lookups still work.
            bool looksLikeHost =
                IPAddress.TryParse(host, out _) ||
                (host.Contains('.') && host[0] != '.' && host[^1] != '.');

            return looksLikeHost ? @"\\" + s.Replace('/', '\\') : input;
        }

        private Task GoBackAsync()
        {
            if (_backHistory.Count == 0) return Task.CompletedTask;
            var prev = _backHistory[^1];
            _backHistory.RemoveAt(_backHistory.Count - 1);
            if (!string.IsNullOrEmpty(_currentPath))
                _forwardHistory.Add(_currentPath);
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
            CommandManager.InvalidateRequerySuggested();
            return NavigateInternalAsync(prev, fromHistory: true);
        }

        private Task GoForwardAsync()
        {
            if (_forwardHistory.Count == 0) return Task.CompletedTask;
            var next = _forwardHistory[^1];
            _forwardHistory.RemoveAt(_forwardHistory.Count - 1);
            if (!string.IsNullOrEmpty(_currentPath))
                _backHistory.Add(_currentPath);
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
            CommandManager.InvalidateRequerySuggested();
            return NavigateInternalAsync(next, fromHistory: true);
        }

        private Task NavigateUpAsync()
        {
            try
            {
                if (IsRemote)
                {
                    var p = RemoteParent(_currentPath);
                    if (p != null) return NavigateAsync(p);
                }
                else
                {
                    var parent = Path.GetDirectoryName(_currentPath);
                    if (!string.IsNullOrEmpty(parent))
                        return NavigateAsync(parent);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Pane", $"NavigateUp failed from {_currentPath}", ex);
            }
            return Task.CompletedTask;
        }

        private bool CanNavigateUp()
        {
            if (string.IsNullOrEmpty(_currentPath)) return false;
            if (IsRemote) return RemoteParent(_currentPath) != null;
            try { return !string.IsNullOrEmpty(Path.GetDirectoryName(_currentPath)); }
            catch { return false; }
        }

        private static string? RemoteParent(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "/") return null;
            var trimmed = path.TrimEnd('/');
            var i = trimmed.LastIndexOf('/');
            if (i <= 0) return "/";
            return trimmed.Substring(0, i);
        }

        public void AttachRemoteSite(Models.FtpSite site)
        {
            Site = site;
            FileSystem = new Business.Ftp.RemoteFileSystemProvider(site);
            CurrentMode = Models.ViewMode.List;
            OnPropertyChanged(nameof(TabTitle));
            OnPropertyChanged(nameof(DriveLetter));
        }

        // Spec §8 line 521: 3 reconnect attempts with exponential backoff
        // (1s, 4s, 16s) before giving up. Status flips to Reconnecting between
        // attempts so the header dot turns amber. CancellationToken short-
        // circuits the backoff sleeps so the user can quit a hung reconnect.
        private static readonly TimeSpan[] ReconnectBackoff =
        {
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(16),
        };

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            if (Site == null) return;

            for (int attempt = 0; attempt <= ReconnectBackoff.Length; attempt++)
            {
                ConnectionStatus = attempt == 0
                    ? Models.ConnectionStatus.Connecting
                    : Models.ConnectionStatus.Reconnecting;

                try
                {
                    await using (var lease = await Business.Ftp.RemoteConnectionPool.AcquireAsync(Site, ct).ConfigureAwait(true))
                    {
                        ConnectionStatus = Models.ConnectionStatus.Connected;
                        Site.LastUsedUtc = DateTime.UtcNow;
                        Business.SiteManagerComponent.Upsert(Site);
                    }

                    var startDir = string.IsNullOrEmpty(Site.StartDirectory) ? "/" : Site.StartDirectory;
                    await NavigateAsync(startDir);
                    return;
                }
                catch (OperationCanceledException)
                {
                    ConnectionStatus = Models.ConnectionStatus.Disconnected;
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warn("Pane",
                        $"Connect attempt {attempt + 1} failed for {Site.Username}@{Site.Host}: {ex.Message}");

                    if (attempt >= ReconnectBackoff.Length)
                    {
                        ConnectionStatus = Models.ConnectionStatus.Error;
                        Services.AppServices.Toast.Error(
                            $"Connect failed after {attempt + 1} attempts: {ex.Message}");
                        return;
                    }

                    try { await Task.Delay(ReconnectBackoff[attempt], ct).ConfigureAwait(true); }
                    catch (OperationCanceledException)
                    {
                        ConnectionStatus = Models.ConnectionStatus.Disconnected;
                        return;
                    }
                }
            }
        }

        public async Task DisconnectAsync()
        {
            if (Site == null) return;
            await Business.Ftp.RemoteConnectionPool.DisconnectAllAsync(Site.Id).ConfigureAwait(true);
            ConnectionStatus = Models.ConnectionStatus.Disconnected;
        }
    }
}
