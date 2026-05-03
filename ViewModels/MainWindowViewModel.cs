using Josha.Business;
using Josha.Models;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Josha.ViewModels
{
    internal class MainWindowViewModel : BaseViewModel
    {
        private bool _isScanning;
        private string _statusText = "Ready";
        private string _loadingTitle = "Loading";
        private string _loadingSubtitle = "";
        private DirectoryTreeItemViewModel? _leftTreeRoot;
        private DirectoryTreeItemViewModel? _rightTreeRoot;
        private DirOD? _rootDirOD;

        private string _namespaceName = "";
        private DANamespace? _selectedNamespace;
        private bool _isCapturingKey;
        private string _bindingDisplayText = "";
        private bool _isScrollMode;
        private bool _isScanComplete;
        private readonly Dictionary<int, string> _bindingMap = new();
        private readonly Dictionary<string, int> _reverseBindingMap = new(StringComparer.OrdinalIgnoreCase);

        private CancellationTokenSource? _backgroundScanCts;

        private CancellationTokenSource? _snapshotSaveCts;
        private readonly SemaphoreSlim _snapshotSaveSem = new(1, 1);

        private CancellationTokenSource? _allSearchCts;
        private CancellationTokenSource? _fileSearchCts;
        private CancellationTokenSource? _dirSearchCts;
        private string _allSearchText = "";
        private string _fileSearchText = "";
        private string _dirSearchText = "";
        private bool _isAllSearchOpen;
        private bool _isFileSearchOpen;
        private bool _isDirSearchOpen;
        private SearchResult? _selectedAllResult;
        private SearchResult? _selectedFileResult;
        private SearchResult? _selectedDirResult;

        public DirectoryTreeItemViewModel? LeftTreeRoot
        {
            get => _leftTreeRoot;
            set { _leftTreeRoot = value; OnPropertyChanged(); }
        }

        public DirectoryTreeItemViewModel? RightTreeRoot
        {
            get => _rightTreeRoot;
            set { _rightTreeRoot = value; OnPropertyChanged(); }
        }

        public string NamespaceName
        {
            get => _namespaceName;
            set { _namespaceName = value; OnPropertyChanged(); }
        }

        public ObservableCollection<DANamespace> Namespaces { get; } = [];

        public DANamespace? SelectedNamespace
        {
            get => _selectedNamespace;
            set
            {
                if (_selectedNamespace == value) return;
                _selectedNamespace = value;
                OnPropertyChanged();

                if (value != null)
                    ApplyNamespace(value);

                UpdateBindingDisplay();
            }
        }

        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                _isScanning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotScanning));
            }
        }

        public bool IsNotScanning => !_isScanning;

        public bool IsCapturingKey
        {
            get => _isCapturingKey;
            set { _isCapturingKey = value; OnPropertyChanged(); }
        }

        public string BindingDisplayText
        {
            get => _bindingDisplayText;
            set { _bindingDisplayText = value; OnPropertyChanged(); }
        }

        public bool IsScrollMode
        {
            get => _isScrollMode;
            set { _isScrollMode = value; OnPropertyChanged(); }
        }

        public bool IsScanComplete
        {
            get => _isScanComplete;
            set { _isScanComplete = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public string LoadingTitle
        {
            get => _loadingTitle;
            set { _loadingTitle = value; OnPropertyChanged(); }
        }

        public string LoadingSubtitle
        {
            get => _loadingSubtitle;
            set { _loadingSubtitle = value; OnPropertyChanged(); }
        }

        public string AllSearchText
        {
            get => _allSearchText;
            set
            {
                if (_allSearchText == value) return;
                _allSearchText = value;
                OnPropertyChanged();

                if (string.IsNullOrEmpty(value))
                {
                    _allSearchCts?.Cancel();
                    AllSearchSuggestions.Clear();
                    IsAllSearchOpen = false;
                }
                else
                {
                    _ = DebounceSearchAsync(value, null,
                        _allSearchCts, cts => _allSearchCts = cts,
                        AllSearchSuggestions, open => IsAllSearchOpen = open);
                }
            }
        }

        public ObservableCollection<SearchResult> AllSearchSuggestions { get; } = [];

        public bool IsAllSearchOpen
        {
            get => _isAllSearchOpen;
            set { _isAllSearchOpen = value; OnPropertyChanged(); }
        }

        public SearchResult? SelectedAllResult
        {
            get => _selectedAllResult;
            set
            {
                if (value == null && _selectedAllResult == null) return;
                _selectedAllResult = value;
                OnPropertyChanged();

                if (value != null)
                {
                    NavigateRequested?.Invoke(value);
                    AllSearchText = "";
                }
            }
        }

        public string FileSearchText
        {
            get => _fileSearchText;
            set
            {
                if (_fileSearchText == value) return;
                _fileSearchText = value;
                OnPropertyChanged();

                if (string.IsNullOrEmpty(value))
                {
                    _fileSearchCts?.Cancel();
                    FileSearchSuggestions.Clear();
                    IsFileSearchOpen = false;
                }
                else
                {
                    _ = DebounceSearchAsync(value, SearchResultType.File,
                        _fileSearchCts, cts => _fileSearchCts = cts,
                        FileSearchSuggestions, open => IsFileSearchOpen = open);
                }
            }
        }

        public ObservableCollection<SearchResult> FileSearchSuggestions { get; } = [];

        public bool IsFileSearchOpen
        {
            get => _isFileSearchOpen;
            set { _isFileSearchOpen = value; OnPropertyChanged(); }
        }

        public SearchResult? SelectedFileResult
        {
            get => _selectedFileResult;
            set
            {
                if (value == null && _selectedFileResult == null) return;
                _selectedFileResult = value;
                OnPropertyChanged();

                if (value != null)
                {
                    NavigateRequested?.Invoke(value);
                    FileSearchText = "";
                }
            }
        }

        public string DirSearchText
        {
            get => _dirSearchText;
            set
            {
                if (_dirSearchText == value) return;
                _dirSearchText = value;
                OnPropertyChanged();

                if (string.IsNullOrEmpty(value))
                {
                    _dirSearchCts?.Cancel();
                    DirSearchSuggestions.Clear();
                    IsDirSearchOpen = false;
                }
                else
                {
                    _ = DebounceSearchAsync(value, SearchResultType.Directory,
                        _dirSearchCts, cts => _dirSearchCts = cts,
                        DirSearchSuggestions, open => IsDirSearchOpen = open);
                }
            }
        }

        public ObservableCollection<SearchResult> DirSearchSuggestions { get; } = [];

        public bool IsDirSearchOpen
        {
            get => _isDirSearchOpen;
            set { _isDirSearchOpen = value; OnPropertyChanged(); }
        }

        public SearchResult? SelectedDirResult
        {
            get => _selectedDirResult;
            set
            {
                if (value == null && _selectedDirResult == null) return;
                _selectedDirResult = value;
                OnPropertyChanged();

                if (value != null)
                {
                    NavigateRequested?.Invoke(value);
                    DirSearchText = "";
                }
            }
        }

        internal event Action<SearchResult>? NavigateRequested;
        internal event Action? NamespaceApplied;
        internal event Action? ScanCompleted;

        public ICommand SaveNamespaceCommand { get; }
        public ICommand BindKeyCommand { get; }

        public MainWindowViewModel()
        {
            SaveNamespaceCommand = new RelayCommand(
                async _ => await SaveNamespaceAsync(),
                _ => !IsScanning && _rootDirOD != null && !string.IsNullOrWhiteSpace(_namespaceName));
            BindKeyCommand = new RelayCommand(
                _ => { if (IsCapturingKey) CancelKeyCapture(); else IsCapturingKey = true; },
                _ => SelectedNamespace != null);

            SnapshotComponent.SnapshotChanged += OnSnapshotChanged;

            _ = AutoScanAsync();
        }

        // Reconciliation (full at startup or per-folder on expand) raises this when
        // it observes that the cached tree no longer matches disk. Coalesce bursts
        // into a single write — folder expansions can fire many events in seconds.
        private void OnSnapshotChanged()
        {
            var root = _rootDirOD;
            if (root == null) return;

            var oldCts = Interlocked.Exchange(ref _snapshotSaveCts, new CancellationTokenSource());
            oldCts?.Cancel();
            var token = _snapshotSaveCts!.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000, token);
                }
                catch (OperationCanceledException) { return; }

                await _snapshotSaveSem.WaitAsync();
                try
                {
                    if (token.IsCancellationRequested) return;
                    SnapshotComponent.SaveSnapshot(root);
                }
                finally
                {
                    _snapshotSaveSem.Release();
                }
            });
        }

        private async Task DebounceSearchAsync(
            string text,
            SearchResultType? typeFilter,
            CancellationTokenSource? oldCts,
            Action<CancellationTokenSource> setCts,
            ObservableCollection<SearchResult> suggestions,
            Action<bool> setOpen)
        {
            oldCts?.Cancel();
            var cts = new CancellationTokenSource();
            setCts(cts);
            var token = cts.Token;

            try
            {
                await Task.Delay(300, token);

                if (_rootDirOD == null) return;

                var sorted = await Task.Run(() =>
                {
                    var component = new SearchComponent();
                    component.Search(text, _rootDirOD);
                    IEnumerable<SearchResult> results = component.SearchResults;
                    if (typeFilter.HasValue)
                        results = results.Where(r => r.ResultType == typeFilter.Value);
                    return results
                        .OrderByDescending(r => r.ResultType)
                        .ThenByDescending(r => MatchScore(r.Name, text))
                        .ToList();
                }, token);

                if (token.IsCancellationRequested) return;

                suggestions.Clear();
                foreach (var r in sorted)
                    suggestions.Add(r);

                setOpen(suggestions.Count > 0);
            }
            catch (OperationCanceledException) { }
        }

        private async Task AutoScanAsync()
        {
            try
            {
                IsScanning = true;
                LoadingTitle = "Loading disk tree";
                LoadingSubtitle = "Reading the saved snapshot...";
                StatusText = "";

                var cachedRoot = await Task.Run(SnapshotComponent.LoadSnapshot);

                if (cachedRoot != null)
                {
                    await RunReconcileAsync(cachedRoot);
                    return;
                }

                // No snapshot — do a single full scan. The loading view stays visible
                // throughout, so we run at normal thread-pool priority for max speed.
                LoadingTitle = "Analyzing your disk";
                LoadingSubtitle = "Reading directories and files in C:\\. This may take a few minutes on first run.";

                var dirAnalyser = new DirectoryAnalyserComponent();
                _backgroundScanCts = new CancellationTokenSource();
                var ct = _backgroundScanCts.Token;

                using var progressCts = new CancellationTokenSource();
                var progressTask = PollProgressAsync(dirAnalyser, progressCts.Token);

                try
                {
                    await Task.Factory.StartNew(() =>
                    {
                        dirAnalyser.Run(ct);
                    }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }
                catch (OperationCanceledException)
                {
                    progressCts.Cancel();
                    IsScanning = false;
                    return;
                }

                progressCts.Cancel();
                await progressTask;

                if (ct.IsCancellationRequested || dirAnalyser.Root == null)
                {
                    IsScanning = false;
                    StatusText = "Scan failed.";
                    return;
                }

                _rootDirOD = dirAnalyser.Root;
                LeftTreeRoot = new DirectoryTreeItemViewModel(dirAnalyser.Root, 0);
                RightTreeRoot = new DirectoryTreeItemViewModel(dirAnalyser.Root, 0);
                IsScanComplete = true;
                IsScanning = false;
                StatusText = $"Scan complete — {FormatSize(dirAnalyser.Root.SizeKiloBytes)} | " +
                             $"{dirAnalyser.DirectoriesScanned:N0} directories, " +
                             $"{dirAnalyser.FilesScanned:N0} files";

                await Task.Run(() => SnapshotComponent.SaveSnapshot(dirAnalyser.Root));

                ScanCompleted?.Invoke();
                await LoadNamespacesAsync();
            }
            catch (Exception ex)
            {
                IsScanning = false;
                StatusText = $"Auto-scan failed: {ex.Message}";
            }
        }

        private async Task RunReconcileAsync(DirOD cachedRoot)
        {
            // Verify the cached tree against the current disk state BEFORE showing it
            // — the user shouldn't see a stale tree even briefly.
            _rootDirOD = cachedRoot;
            LoadingTitle = "Verifying disk tree";
            LoadingSubtitle = "Checking for changes since the last scan...";
            StatusText = "";

            _backgroundScanCts = new CancellationTokenSource();
            var ct = _backgroundScanCts.Token;
            var progress = new ScanCore.ScanProgress();

            using var progressCts = new CancellationTokenSource();
            var progressTask = PollReconcileProgressAsync(progress, progressCts.Token);

            try
            {
                await Task.Factory.StartNew(() =>
                {
                    SnapshotComponent.Reconcile(cachedRoot, ct, progress);
                }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            catch (OperationCanceledException)
            {
                progressCts.Cancel();
                IsScanning = false;
                return;
            }

            progressCts.Cancel();
            await progressTask;

            if (ct.IsCancellationRequested)
            {
                IsScanning = false;
                return;
            }

            LeftTreeRoot = new DirectoryTreeItemViewModel(cachedRoot, 0);
            RightTreeRoot = new DirectoryTreeItemViewModel(cachedRoot, 0);
            IsScanComplete = true;
            IsScanning = false;
            StatusText = $"Verified — {FormatSize(cachedRoot.SizeKiloBytes)} | " +
                         $"{progress.Directories:N0} directories, " +
                         $"{progress.Files:N0} files" +
                         (progress.Changes > 0 ? $" | {progress.Changes:N0} changes" : "");

            // No explicit save here — SnapshotComponent.Reconcile fires
            // SnapshotChanged when progress.Changes > 0, and OnSnapshotChanged
            // schedules the debounced write.

            ScanCompleted?.Invoke();
            await LoadNamespacesAsync();
        }

        private async Task PollReconcileProgressAsync(
            ScanCore.ScanProgress progress,
            CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(500, ct);

                    int dirs = progress.Directories;
                    int files = progress.Files;

                    if (files > 0)
                        StatusText = $"Verifying... {files:N0} files | {dirs:N0} directories";
                    else if (dirs > 0)
                        StatusText = $"Verifying... {dirs:N0} directories";
                    else
                        StatusText = "";
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task SaveNamespaceAsync()
        {
            if (string.IsNullOrWhiteSpace(_namespaceName) || _rootDirOD == null) return;

            var name = _namespaceName.Trim();
            var expandedOne = CollectExpandedDirs(_leftTreeRoot);
            var expandedTwo = CollectExpandedDirs(_rightTreeRoot);

            await Task.Run(() => NamespaceComponent.CreateNamespace(name, expandedOne, expandedTwo));

            NamespaceName = "";
            StatusText = $"Namespace \"{name}\" saved.";

            await LoadNamespacesAsync();
        }

        private async Task LoadNamespacesAsync()
        {
            var loaded = await Task.Run(NamespaceComponent.LoadNamespaces);

            if(!loaded.Any(x => x.Name == "default"))
            {
                NamespaceComponent.CreateNamespace("default", [.. _rootDirOD!.Subdirectories], [.. _rootDirOD!.Subdirectories], defaultNamespace: true);

                loaded = await Task.Run(NamespaceComponent.LoadNamespaces);
            }

            SelectedNamespace = null;
            Namespaces.Clear();
            foreach (var ns in loaded)
                Namespaces.Add(ns);

            await LoadBindingsAsync();
        }

        private void ApplyNamespace(DANamespace ns)
        {
            if (_rootDirOD == null) return;

            var pathSetOne = new HashSet<string>(
                ns.ChildNodesTreeOne.Select(d => d.Path), StringComparer.OrdinalIgnoreCase);
            var pathSetTwo = new HashSet<string>(
                ns.ChildNodesTreeTwo.Select(d => d.Path), StringComparer.OrdinalIgnoreCase);

            var leftRoot = new DirectoryTreeItemViewModel(_rootDirOD, 0);
            var rightRoot = new DirectoryTreeItemViewModel(_rootDirOD, 0);

            ExpandPaths(leftRoot, pathSetOne);
            ExpandPaths(rightRoot, pathSetTwo);

            LeftTreeRoot = leftRoot;
            RightTreeRoot = rightRoot;

            NamespaceApplied?.Invoke();
        }

        private static void ExpandPaths(DirectoryTreeItemViewModel node, HashSet<string> paths)
        {
            if (node.Model == null || !paths.Contains(node.Model.Path)) return;

            if (!node.IsExpanded && node.HasContent)
                node.IsExpanded = true;

            foreach (var child in node.Children)
            {
                if (child is DirectoryTreeItemViewModel dirChild)
                    ExpandPaths(dirChild, paths);
            }
        }

        private static List<DirOD> CollectExpandedDirs(DirectoryTreeItemViewModel? root)
        {
            var result = new List<DirOD>();
            if (root != null)
                CollectExpandedRecursive(root, result);
            return result;
        }

        private static void CollectExpandedRecursive(TreeItemViewModel node, List<DirOD> result)
        {
            if (node is DirectoryTreeItemViewModel dir && dir.IsExpanded && dir.Model != null)
            {
                result.Add(dir.Model);
                foreach (var child in dir.Children)
                    CollectExpandedRecursive(child, result);
            }
        }

        private async Task PollProgressAsync(
            DirectoryAnalyserComponent dirAnalyser,
            CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(500, ct);

                    int dirs = dirAnalyser.DirectoriesScanned;
                    int files = dirAnalyser.FilesScanned;

                    if (files > 0)
                        StatusText = $"Scanning... {files:N0} files | {dirs:N0} directories";
                    else if (dirs > 0)
                        StatusText = $"Scanning directories... {dirs:N0} found";
                    else
                        StatusText = "Scanning directories...";
                }
            }
            catch (OperationCanceledException) { }
        }

        private static double MatchScore(string name, string query)
        {
            if (name.Equals(query, StringComparison.OrdinalIgnoreCase))
                return 1.0;
            if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                return 0.8 + (double)query.Length / name.Length * 0.2;
            return (double)query.Length / name.Length;
        }

        private async Task LoadBindingsAsync()
        {
            var bindings = await Task.Run(NamespaceComponent.LoadBindings);

            _bindingMap.Clear();
            _reverseBindingMap.Clear();

            foreach (var b in bindings)
            {
                if (_bindingMap.TryGetValue(b.KeyCode, out var oldNs))
                    _reverseBindingMap.Remove(oldNs);
                if (_reverseBindingMap.TryGetValue(b.NamespaceName, out var oldKey))
                    _bindingMap.Remove(oldKey);

                _bindingMap[b.KeyCode] = b.NamespaceName;
                _reverseBindingMap[b.NamespaceName] = b.KeyCode;
            }

            UpdateBindingDisplay();
        }

        internal async Task CaptureKeyAsync(int keyCode)
        {
            if (SelectedNamespace == null) return;

            var nsName = SelectedNamespace.Name;

            // Clear old binding for this namespace
            if (_reverseBindingMap.TryGetValue(nsName, out var oldKeyCode))
            {
                _bindingMap.Remove(oldKeyCode);
                _reverseBindingMap.Remove(nsName);
            }

            // Clear conflicting binding (another namespace using this key)
            if (_bindingMap.TryGetValue(keyCode, out var conflictNs))
            {
                _reverseBindingMap.Remove(conflictNs);
                _bindingMap.Remove(keyCode);
            }

            _bindingMap[keyCode] = nsName;
            _reverseBindingMap[nsName] = keyCode;

            await Task.Run(() => NamespaceComponent.CreateBinding(keyCode, nsName));

            IsCapturingKey = false;
            UpdateBindingDisplay();
            StatusText = $"Bound Ctrl+{FormatKeyName(keyCode)} → \"{nsName}\"";
        }

        internal void CancelKeyCapture()
        {
            IsCapturingKey = false;
            UpdateBindingDisplay();
        }

        internal bool TryApplyBinding(int keyCode)
        {
            if (!_bindingMap.TryGetValue(keyCode, out var nsName))
                return false;

            var ns = Namespaces.FirstOrDefault(n =>
                string.Equals(n.Name, nsName, StringComparison.OrdinalIgnoreCase));
            if (ns == null)
                return false;

            SelectedNamespace = ns;
            return true;
        }

        private void UpdateBindingDisplay()
        {
            if (_selectedNamespace != null &&
                _reverseBindingMap.TryGetValue(_selectedNamespace.Name, out var keyCode))
            {
                BindingDisplayText = "Ctrl+" + FormatKeyName(keyCode);
            }
            else
            {
                BindingDisplayText = "";
            }
        }

        private static string FormatKeyName(int keyCode)
        {
            var key = (Key)keyCode;
            return key switch
            {
                >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
                >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (char)('0' + (key - Key.NumPad0)),
                >= Key.F1 and <= Key.F24 => "F" + (1 + key - Key.F1),
                _ => key.ToString()
            };
        }

        private static string FormatSize(decimal sizeKB)
        {
            if (sizeKB < 1000)
                return $"{sizeKB:N0} KB";
            if (sizeKB < 1_000_000)
                return $"{sizeKB / 1000:N1} MB";
            return $"{sizeKB / 1_000_000:N2} GB";
        }
    }
}
