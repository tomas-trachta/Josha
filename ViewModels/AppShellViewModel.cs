using Josha.Business;
using Josha.Models;
using Josha.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Josha.ViewModels
{
    internal class AppShellViewModel : BaseViewModel
    {
        private string _statusText = "Ready";
        private string _clock = "";
        private PaneColumnViewModel _activeColumn;

        public PaneColumnViewModel LeftColumn { get; }
        public PaneColumnViewModel RightColumn { get; }

        public PaneColumnViewModel ActiveColumn
        {
            get => _activeColumn;
            private set
            {
                if (_activeColumn == value) return;
                _activeColumn = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActivePane));
                OnPropertyChanged(nameof(InactivePane));
                OnPropertyChanged(nameof(InactiveColumn));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public PaneColumnViewModel InactiveColumn =>
            _activeColumn == LeftColumn ? RightColumn : LeftColumn;

        public FilePaneViewModel? ActivePane => _activeColumn?.ActiveTab;
        public FilePaneViewModel? InactivePane => InactiveColumn?.ActiveTab;

        public string StatusText
        {
            get => _statusText;
            set { if (_statusText == value) return; _statusText = value; OnPropertyChanged(); }
        }

        public string Clock
        {
            get => _clock;
            private set { if (_clock == value) return; _clock = value; OnPropertyChanged(); }
        }

        public ICommand CopyCommand { get; }
        public ICommand MoveCommand { get; }
        public ICommand MkdirCommand { get; }
        public ICommand NewFileCommand { get; }
        public ICommand RenameCommand { get; }
        public ICommand DeleteRecycleCommand { get; }
        public ICommand DeletePermanentCommand { get; }
        public ICommand RefreshActiveCommand { get; }
        public ICommand SetActiveLeftCommand { get; }
        public ICommand SetActiveRightCommand { get; }
        public ICommand CopyPathCommand { get; }
        public ICommand CopyNameCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand ViewCommand { get; }
        public ICommand SelectByPatternCommand { get; }
        public ICommand DeselectByPatternCommand { get; }
        public ICommand InvertSelectionCommand { get; }
        public ICommand TogglePreviewPaneCommand { get; }

        // MainWindow wires this — VM raises a ("title", localPathToView) request,
        // shell shows the InternalViewer modal with that file. Same Func-as-event
        // pattern as OverwriteResolver.
        public Action<string, string>? ViewFileRequested { get; set; }

        // For pattern select / deselect — MainWindow renders a small prompt
        // modal and returns the typed pattern (or null on cancel).
        public Func<string, string?>? PatternPromptRequested { get; set; }

        public ICommand NewTabCommand { get; }
        public ICommand CloseTabCommand { get; }
        public ICommand NextTabCommand { get; }
        public ICommand PrevTabCommand { get; }
        public ICommand PasteCommand { get; }
        public ICommand AddBookmarkCommand { get; }
        public ICommand OpenBookmarksCommand { get; }
        public ICommand RemoveBookmarkCommand { get; }
        public ICommand NavigateToBookmarkCommand { get; }

        public ICommand OpenNewConnectionCommand { get; }
        public ICommand OpenSiteManagerCommand { get; }
        public ICommand QuickConnectCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand OpenCommandPaletteCommand { get; }

        public Func<bool>? SettingsRequested { get; set; }
        public Func<IEnumerable<CommandPaletteItem>, CommandPaletteItem?>? CommandPaletteRequested { get; set; }

        public ObservableCollection<Bookmark> Bookmarks { get; } = new();

        // Raised when the OpenBookmarksCommand fires; MainWindow shows the picker.
        // Plain .NET event so the VM stays free of WPF Window dependencies.
        public event Action? BookmarksPickerRequested;

        // Set by MainWindow on startup. The VM raises these to surface the
        // dialogs without taking a Window dependency. Same pattern as
        // OverwriteResolver — Func instead of event so callers can swap in a
        // single resolver from outside.
        public Func<FtpSite?>? NewConnectionRequested { get; set; }
        public Func<FtpSite?>? SiteManagerRequested { get; set; }

        // Set by MainWindow on startup to delegate the overwrite-confirm modal
        // out of the VM. If unset, ResolveOverwrites cancels by default.
        public Func<IReadOnlyList<string>, OverwriteResolution>? OverwriteResolver { get; set; }

        public AppShellViewModel()
        {
            LeftColumn = new PaneColumnViewModel(@"C:\");
            RightColumn = new PaneColumnViewModel(@"C:\");
            _activeColumn = LeftColumn;
            SyncActiveStates();

            // The status-bar bindings depend on ActivePane, which depends on
            // ActiveColumn.ActiveTab — re-fire them whenever a tab changes.
            LeftColumn.PropertyChanged  += OnColumnPropertyChanged;
            RightColumn.PropertyChanged += OnColumnPropertyChanged;

            Clock = DateTime.Now.ToString("HH:mm");
            var clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            clockTimer.Tick += (_, _) => Clock = DateTime.Now.ToString("HH:mm");
            clockTimer.Start();

            CopyCommand              = new RelayCommand(_ => _ = CopySelectedAsync(),                _ => HasAnySelection());
            MoveCommand              = new RelayCommand(_ => _ = MoveSelectedAsync(),                _ => HasAnySelection());
            MkdirCommand             = new RelayCommand(_ => _ = NewFolderAsync());
            NewFileCommand           = new RelayCommand(_ => _ = NewFileAsync(),  _ => ActivePane != null && !string.IsNullOrEmpty(ActivePane.CurrentPath));
            RenameCommand            = new RelayCommand(_ => _ = RenameAsync(),                     _ => HasSingleFileOrDirSelection());
            DeleteRecycleCommand     = new RelayCommand(_ => _ = DeleteSelectedAsync(toRecycle: true),  _ => HasAnySelection());
            DeletePermanentCommand   = new RelayCommand(_ => _ = DeleteSelectedAsync(toRecycle: false), _ => HasAnySelection());
            RefreshActiveCommand     = new RelayCommand(_ => { if (ActivePane != null) _ = ActivePane.List.RefreshAsync(); });

            SetActiveLeftCommand     = new RelayCommand(_ => SetActiveColumn(LeftColumn));
            SetActiveRightCommand    = new RelayCommand(_ => SetActiveColumn(RightColumn));

            CopyPathCommand          = new RelayCommand(_ => CopySelectionToClipboard(useFullPath: true),  _ => HasAnySelection());
            CopyNameCommand          = new RelayCommand(_ => CopySelectionToClipboard(useFullPath: false), _ => HasAnySelection());

            EditCommand              = new RelayCommand(_ => EditSelected(), _ => HasSingleFileSelection());
            ViewCommand              = new RelayCommand(_ => _ = ViewSelectedAsync(), _ => HasSingleFileSelection());
            SelectByPatternCommand   = new RelayCommand(_ => ApplyPattern(select: true),  _ => ActivePane != null && ActivePane.CurrentMode == ViewMode.List);
            DeselectByPatternCommand = new RelayCommand(_ => ApplyPattern(select: false), _ => ActivePane != null && ActivePane.CurrentMode == ViewMode.List);
            InvertSelectionCommand   = new RelayCommand(_ => InvertSelection(),           _ => ActivePane != null && ActivePane.CurrentMode == ViewMode.List);
            TogglePreviewPaneCommand = new RelayCommand(_ => { if (ActivePane != null) ActivePane.IsPreviewPaneVisible = !ActivePane.IsPreviewPaneVisible; }, _ => ActivePane != null);
            OpenSettingsCommand      = new RelayCommand(_ => SettingsRequested?.Invoke());
            OpenCommandPaletteCommand = new RelayCommand(_ => OpenCommandPalette());

            NewTabCommand            = new RelayCommand(_ => _activeColumn.AddTab(_activeColumn.ActiveTab?.CurrentPath ?? @"C:\"));
            CloseTabCommand          = new RelayCommand(_ => _activeColumn.CloseTab(_activeColumn.ActiveTab), _ => _activeColumn.Tabs.Count > 1);
            NextTabCommand           = new RelayCommand(_ => _activeColumn.NextTabCommand.Execute(null), _ => _activeColumn.Tabs.Count > 1);
            PrevTabCommand           = new RelayCommand(_ => _activeColumn.PrevTabCommand.Execute(null), _ => _activeColumn.Tabs.Count > 1);

            PasteCommand             = new RelayCommand(_ => _ = PasteFromClipboardAsync(), _ => CanPasteFromClipboard());

            AddBookmarkCommand       = new RelayCommand(_ => AddBookmarkForActivePane(),  _ => ActivePane != null && !string.IsNullOrEmpty(ActivePane.CurrentPath));
            OpenBookmarksCommand     = new RelayCommand(_ => BookmarksPickerRequested?.Invoke());
            RemoveBookmarkCommand    = new RelayCommand(b => RemoveBookmark(b as Bookmark));
            NavigateToBookmarkCommand = new RelayCommand(b => NavigateToBookmark(b as Bookmark));

            OpenNewConnectionCommand = new RelayCommand(_ => RaiseNewConnection());
            OpenSiteManagerCommand   = new RelayCommand(_ => RaiseSiteManager());
            QuickConnectCommand      = new RelayCommand(idx => QuickConnect(idx));

            LoadBookmarksFromDisk();
        }

        public void OpenRemoteTab(FtpSite site)
        {
            _activeColumn?.AddRemoteTab(site);
        }

        private void RaiseNewConnection()
        {
            var site = NewConnectionRequested?.Invoke();
            if (site != null) OpenRemoteTab(site);
        }

        private void RaiseSiteManager()
        {
            var site = SiteManagerRequested?.Invoke();
            if (site != null) OpenRemoteTab(site);
        }

        private void QuickConnect(object? indexParam)
        {
            int idx;
            if (indexParam is int i) idx = i;
            else if (indexParam is string s && int.TryParse(s, out var n)) idx = n;
            else return;
            if (idx < 1 || idx > 9) return;

            var sites = SiteManagerComponent.Load()
                .OrderByDescending(x => x.LastUsedUtc)
                .ToList();
            if (idx > sites.Count) { AppServices.Toast.Info($"No saved site at slot {idx}"); return; }
            OpenRemoteTab(sites[idx - 1]);
        }

        private void LoadBookmarksFromDisk()
        {
            try
            {
                Bookmarks.Clear();
                foreach (var b in BookmarkComponent.Load())
                    Bookmarks.Add(b);
            }
            catch (Exception ex)
            {
                Log.Warn("Shell", "Bookmarks load failed", ex);
            }
        }

        private void AddBookmarkForActivePane()
        {
            if (ActivePane == null) return;
            var path = ActivePane.CurrentPath;
            if (string.IsNullOrEmpty(path)) return;

            // Skip exact-path duplicates so re-firing Ctrl+D doesn't pile up.
            if (Bookmarks.Any(b => string.Equals(b.TargetPath, path, StringComparison.OrdinalIgnoreCase)))
            {
                AppServices.Toast.Info($"Bookmark already exists: {Path.GetFileName(path.TrimEnd('\\', '/'))}");
                return;
            }

            var name = Path.GetFileName(path.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(name)) name = path;

            Bookmarks.Add(new Bookmark(name, path));
            BookmarkComponent.Save(Bookmarks);
            AppServices.Toast.Success($"Bookmarked '{name}'");
        }

        private void RemoveBookmark(Bookmark? bookmark)
        {
            if (bookmark == null) return;
            if (Bookmarks.Remove(bookmark))
                BookmarkComponent.Save(Bookmarks);
        }

        private void NavigateToBookmark(Bookmark? bookmark)
        {
            if (bookmark == null || ActivePane == null) return;
            _ = ActivePane.NavigateAsync(bookmark.TargetPath);
        }

        private bool CanPasteFromClipboard()
        {
            if (ActivePane == null) return false;
            try { return Clipboard.ContainsFileDropList(); }
            catch { return false; }
        }

        private async Task PasteFromClipboardAsync()
        {
            if (ActivePane == null) return;

            System.Collections.Specialized.StringCollection files;
            try { files = Clipboard.GetFileDropList(); }
            catch (Exception ex)
            {
                Log.Warn("Shell", "Clipboard read failed", ex);
                AppServices.Toast.Error("Couldn't read clipboard");
                return;
            }
            if (files.Count == 0) return;

            var dst = ActivePane.CurrentPath;
            if (string.IsNullOrEmpty(dst))
            {
                StatusText = "Paste: pane has no path";
                return;
            }

            // Detect cut vs copy via shell's "Preferred DropEffect" marker. The
            // payload is a 4-byte little-endian DROPEFFECT: 0x02 = MOVE, 0x05 = COPY.
            var isCut = false;
            try
            {
                if (Clipboard.GetData("Preferred DropEffect") is System.IO.MemoryStream ms)
                {
                    var bytes = ms.ToArray();
                    if (bytes.Length >= 1 && (bytes[0] & 0x02) == 0x02 && (bytes[0] & 0x01) == 0)
                        isCut = true;
                }
            }
            catch { }

            // Pre-scan for conflicts; same UX as F5 / F6.
            var pseudoSelection = files.Cast<string>()
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => new ClipboardItem(s))
                .ToList();

            var conflictNames = pseudoSelection
                .Where(p => File.Exists(Path.Combine(dst, p.Name)) || Directory.Exists(Path.Combine(dst, p.Name)))
                .Select(p => p.Name)
                .ToList();

            bool overwrite = false;
            if (conflictNames.Count > 0)
            {
                var resolver = OverwriteResolver;
                var choice = resolver != null ? resolver(conflictNames) : OverwriteResolution.Cancel;
                switch (choice)
                {
                    case OverwriteResolution.Replace:
                        overwrite = true;
                        break;
                    case OverwriteResolution.Skip:
                        var skip = new HashSet<string>(conflictNames, StringComparer.OrdinalIgnoreCase);
                        pseudoSelection = pseudoSelection.Where(p => !skip.Contains(p.Name)).ToList();
                        break;
                    default:
                        StatusText = "Paste cancelled";
                        return;
                }
            }
            if (pseudoSelection.Count == 0) return;

            int ok = 0, fail = 0;
            string? lastError = null;
            foreach (var p in pseudoSelection)
            {
                var dstPath = Path.Combine(dst, p.Name);
                StatusText = (isCut ? "Pasting (move) " : "Pasting ") + p.Name + "…";
                var result = isCut
                    ? await FileOpsComponent.MoveAsync(p.FullPath, dstPath, overwrite: overwrite)
                    : await FileOpsComponent.CopyAsync(p.FullPath, dstPath, overwrite: overwrite);
                if (result.Success) ok++;
                else { fail++; lastError = result.Error; Log.Warn("Shell", $"Paste failed: {p.FullPath}: {result.Error}"); }
            }

            await ActivePane.List.RefreshAsync();
            ReportOpOutcome(isCut ? "Pasted (moved)" : "Pasted", ok, fail, lastError);
        }

        private sealed record ClipboardItem(string FullPath)
        {
            public string Name => Path.GetFileName(FullPath.TrimEnd('\\', '/'));
        }

        private void OnColumnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(PaneColumnViewModel.ActiveTab)) return;
            // Either column's ActiveTab moved (Ctrl+Tab, tab-bar click, close,
            // add). Re-sync IsActive so the displayed tab in the active column
            // gets the blue border and stale flags on no-longer-shown tabs are
            // cleared — without this, two panes can both show "active" at once.
            SyncActiveStates();
            // ActivePane / InactivePane may now point elsewhere — re-fire so
            // bindings (status bar, F-key CanExecute) refresh.
            OnPropertyChanged(nameof(ActivePane));
            OnPropertyChanged(nameof(InactivePane));
            CommandManager.InvalidateRequerySuggested();
        }

        private void EditSelected()
        {
            var selection = SnapshotSelection();
            if (selection.Count != 1)
            {
                StatusText = "Edit: select exactly one file";
                return;
            }

            var row = selection[0];
            if (row.IsDirectory)
            {
                StatusText = "Edit: select a file, not a folder";
                return;
            }

            if (ActivePane != null && ActivePane.IsRemote && ActivePane.Site != null)
            {
                _ = StartRemoteEditAsync(ActivePane.Site, row.FullPath, row.Name);
                return;
            }

            var editor = GetExternalEditor();
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = editor,
                    Arguments = $"\"{row.FullPath}\"",
                    WorkingDirectory = Path.GetDirectoryName(row.FullPath) ?? string.Empty,
                    UseShellExecute = true,
                });
                StatusText = $"Opened {row.Name} in editor";
            }
            catch (Exception ex)
            {
                Log.Warn("Shell", $"Edit failed for {row.FullPath} (editor={editor})", ex);
                AppServices.Toast.Error($"Couldn't open editor: {ex.Message}");
            }
        }

        private async Task ViewSelectedAsync()
        {
            var selection = SnapshotSelection();
            if (selection.Count != 1) { StatusText = "View: select exactly one file"; return; }
            var row = selection[0];
            if (row.IsDirectory) { StatusText = "View: select a file, not a folder"; return; }
            if (ActivePane == null) return;

            var fs = ActivePane.FileSystem;
            string localPath;
            try
            {
                if (!fs.IsRemote)
                {
                    localPath = row.FullPath;
                }
                else
                {
                    AppServices.Toast.Info($"Downloading {row.Name}…");
                    var dir = Path.Combine(Path.GetTempPath(), "Josha", "views", Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dir);
                    localPath = Path.Combine(dir, row.Name);
                    await using var src = await fs.OpenReadAsync(row.FullPath);
                    await using var dst = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None,
                        256 * 1024, useAsync: true);
                    await src.CopyToAsync(dst);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Shell", $"View prep failed for {row.FullPath}", ex);
                AppServices.Toast.Error($"Couldn't open viewer: {ex.Message}");
                return;
            }

            ViewFileRequested?.Invoke(row.Name, localPath);
        }

        private void ApplyPattern(bool select)
        {
            if (ActivePane == null) return;
            var pattern = PatternPromptRequested?.Invoke(select ? "Select files matching:" : "Deselect files matching:");
            if (string.IsNullOrWhiteSpace(pattern)) return;

            System.Text.RegularExpressions.Regex rx;
            try
            {
                var p = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                    .Replace("\\*", ".*").Replace("\\?", ".") + "$";
                rx = new System.Text.RegularExpressions.Regex(p, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            catch (Exception ex) { AppServices.Toast.Error($"Bad pattern: {ex.Message}"); return; }

            int touched = 0;
            foreach (var row in ActivePane.List.Rows)
            {
                if (row.IsParentLink) continue;
                if (!rx.IsMatch(row.Name)) continue;
                if (select && !row.IsSelected) { row.IsSelected = true; touched++; }
                else if (!select && row.IsSelected) { row.IsSelected = false; touched++; }
            }
            StatusText = (select ? "Selected " : "Deselected ") + $"{touched} item{(touched == 1 ? "" : "s")} matching '{pattern}'";
        }

        private void InvertSelection()
        {
            if (ActivePane == null) return;
            int n = 0;
            foreach (var row in ActivePane.List.Rows)
            {
                if (row.IsParentLink) continue;
                row.IsSelected = !row.IsSelected;
                n++;
            }
            StatusText = $"Inverted selection on {n} items";
        }

        private async Task StartRemoteEditAsync(FtpSite site, string remotePath, string name)
        {
            StatusText = $"Downloading {name} for edit…";
            var watcher = await EditOnServerWatcher.StartAsync(site, remotePath);
            if (watcher == null)
            {
                AppServices.Toast.Error($"Couldn't start remote edit for {name}");
                return;
            }
            StatusText = $"Editing {name} — saves will upload back to {site.Host}";
            AppServices.Toast.Success($"Editing '{name}' — saves will upload back");
        }

        // Precedence: Settings → JOSHA_EDITOR → EDITOR → VISUAL → notepad.
        private static string GetExternalEditor()
        {
            var fromSettings = AppServices.Settings.EditorPath;
            if (!string.IsNullOrWhiteSpace(fromSettings)) return fromSettings.Trim();
            return Environment.GetEnvironmentVariable("JOSHA_EDITOR")
                ?? Environment.GetEnvironmentVariable("EDITOR")
                ?? Environment.GetEnvironmentVariable("VISUAL")
                ?? "notepad.exe";
        }

        private bool HasSingleFileSelection()
        {
            if (ActivePane == null) return false;
            if (ActivePane.CurrentMode == ViewMode.DiskUsage)
                return ActivePane.DiskUsageSelection.Count == 1
                    && ActivePane.DiskUsageSelection[0] is FileTreeItemViewModel;
            if (ActivePane.CurrentMode != ViewMode.List) return false;
            return ActivePane.List.SelectedRows.Count(r => !r.IsParentLink && !r.IsDirectory) == 1;
        }

        private List<CommandPaletteItem> BuildPaletteItems()
        {
            var items = new List<CommandPaletteItem>();

            void AddCmd(string title, ICommand cmd, string hotkey, string subtitle = "")
            {
                if (!cmd.CanExecute(null)) return;
                items.Add(new CommandPaletteItem
                {
                    Category = "Action",
                    Title = title,
                    Subtitle = string.IsNullOrEmpty(subtitle) ? hotkey : $"{subtitle} · {hotkey}",
                    Action = () => { if (cmd.CanExecute(null)) cmd.Execute(null); },
                    CategoryOrder = 1,
                });
            }

            AddCmd("Copy",                 CopyCommand,                "F5");
            AddCmd("Move",                 MoveCommand,                "F6");
            AddCmd("New folder",           MkdirCommand,               "F7");
            AddCmd("New file",             NewFileCommand,             "Shift+F4");
            AddCmd("Rename",               RenameCommand,              "F2");
            AddCmd("View file",            ViewCommand,                "F3");
            AddCmd("Edit in external editor", EditCommand,             "F4");
            AddCmd("Move to Recycle Bin",  DeleteRecycleCommand,       "F8");
            AddCmd("Permanent delete",     DeletePermanentCommand,     "Shift+F8");
            AddCmd("Refresh active pane",  RefreshActiveCommand,       "Ctrl+R");
            AddCmd("Toggle preview pane",  TogglePreviewPaneCommand,   "Ctrl+Q");
            AddCmd("New tab",              NewTabCommand,              "Ctrl+T");
            AddCmd("Close tab",            CloseTabCommand,            "Ctrl+W");
            AddCmd("Add bookmark",         AddBookmarkCommand,         "Ctrl+D");
            AddCmd("Open bookmarks",       OpenBookmarksCommand,       "Ctrl+B");
            AddCmd("Copy path",            CopyPathCommand,            "Ctrl+Shift+C");
            AddCmd("Copy name",            CopyNameCommand,            "Ctrl+Alt+C");
            AddCmd("Select by pattern",    SelectByPatternCommand,     "NumPad +");
            AddCmd("Deselect by pattern",  DeselectByPatternCommand,   "NumPad -");
            AddCmd("Invert selection",     InvertSelectionCommand,     "NumPad *");
            AddCmd("New connection…",      OpenNewConnectionCommand,   "Ctrl+Shift+N");
            AddCmd("Site manager…",        OpenSiteManagerCommand,     "Ctrl+Shift+M");
            AddCmd("Settings…",            OpenSettingsCommand,        "Ctrl+,");

            foreach (var bm in Bookmarks)
            {
                var path = bm.TargetPath;
                items.Add(new CommandPaletteItem
                {
                    Category = "Bookmark",
                    Title = bm.Name,
                    Subtitle = path,
                    Action = () => { if (ActivePane != null) _ = ActivePane.NavigateAsync(path); },
                    CategoryOrder = 2,
                });
            }

            try
            {
                var sites = SiteManagerComponent.Load();
                foreach (var site in sites)
                {
                    var captured = site;
                    items.Add(new CommandPaletteItem
                    {
                        Category = "Site",
                        Title = string.IsNullOrEmpty(captured.Name) ? captured.Host : captured.Name,
                        Subtitle = $"{captured.Protocol.ToString().ToLowerInvariant()}://{captured.Username}@{captured.Host}:{captured.Port}",
                        Action = () =>
                        {
                            if (_activeColumn == null) return;
                            var tab = _activeColumn.AddRemoteTab(captured);
                            _activeColumn.ActiveTab = tab;
                            _ = tab.ConnectAsync();
                        },
                        CategoryOrder = 3,
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Shell", "Site list load failed for command palette", ex);
            }

            return items;
        }

        private void OpenCommandPalette()
        {
            if (CommandPaletteRequested == null) return;
            var items = BuildPaletteItems();
            CommandPaletteRequested.Invoke(items);
        }

        private void CopySelectionToClipboard(bool useFullPath)
        {
            var selection = SnapshotSelection();
            if (selection.Count == 0) return;

            var text = string.Join(Environment.NewLine,
                selection.Select(r => useFullPath ? r.FullPath : r.Name));

            try { Clipboard.SetText(text); }
            catch
            {
                try { Thread.Sleep(80); Clipboard.SetText(text); }
                catch (Exception ex)
                {
                    Log.Warn("Shell", "Clipboard write failed", ex);
                    AppServices.Toast.Error("Couldn't write to clipboard");
                    return;
                }
            }

            var label = useFullPath ? "path" : "name";
            AppServices.Toast.Success(selection.Count == 1
                ? $"Copied {label}"
                : $"Copied {selection.Count} {label}s");
        }

        // Called when the user mouses-down inside a pane. The pane belongs to
        // exactly one column; we activate that column and ensure that pane is
        // its current tab (handles cases where focus jumps from a non-active tab
        // via keyboard).
        public void SetActive(FilePaneViewModel pane)
        {
            if (pane == null) return;

            var column = LeftColumn.Tabs.Contains(pane) ? LeftColumn
                       : RightColumn.Tabs.Contains(pane) ? RightColumn
                       : null;
            if (column == null) return;

            column.ActiveTab = pane;
            SetActiveColumn(column);
        }

        public void SetActiveColumn(PaneColumnViewModel column)
        {
            if (column == null) return;
            ActiveColumn = column;
            SyncActiveStates();
        }

        // Single source of truth for IsActive: exactly one tab in the entire
        // app — the active column's ActiveTab — has IsActive=true; every other
        // tab is false. F-key targeting and the pane's blue border both read
        // this flag, so it must stay consistent with the displayed tab in the
        // active column. Called from every code path that can change either
        // ActiveColumn or a column's ActiveTab (Ctrl+Tab, tab-bar click, close
        // tab, add tab, programmatic navigation), so the invariant can't drift.
        private void SyncActiveStates()
        {
            var active = _activeColumn?.ActiveTab;
            foreach (var t in LeftColumn.Tabs)  t.IsActive = ReferenceEquals(t, active);
            foreach (var t in RightColumn.Tabs) t.IsActive = ReferenceEquals(t, active);
        }

        private Task CopySelectedAsync() => EnqueueBatchAsync(FileOperationKind.Copy);
        private Task MoveSelectedAsync() => EnqueueBatchAsync(FileOperationKind.Move);

        private Task EnqueueBatchAsync(FileOperationKind kind)
        {
            if (ActivePane == null || InactivePane == null) return Task.CompletedTask;
            var selection = SnapshotSelection();
            if (selection.Count == 0) return Task.CompletedTask;

            var dst = InactivePane.CurrentPath;
            if (string.IsNullOrEmpty(dst))
            {
                StatusText = $"{kind}: destination pane has no path";
                return Task.CompletedTask;
            }

            var srcFs = ActivePane.FileSystem;
            var dstFs = InactivePane.FileSystem;
            var verb = kind == FileOperationKind.Copy ? "copy" : "move";

            bool overwrite = false;
            if (!dstFs.IsRemote && !ResolveOverwrites(ref selection, dst, verb, out overwrite))
                return Task.CompletedTask;
            if (selection.Count == 0) return Task.CompletedTask;

            // Refresh both panes once after the whole batch, not per-job — saves
            // N redundant LISTs on remote destinations. Fires from queue worker
            // via OnComplete; multiple jobs trigger multiple refreshes which is
            // fine because pane.List has its own dedupe.
            var srcPane = ActivePane;
            var dstPane = InactivePane;
            Action refresh = () =>
            {
                _ = srcPane.List.RefreshAsync();
                _ = dstPane.List.RefreshAsync();
            };

            int enqueued = 0;
            foreach (var row in selection)
            {
                var dstPath = JoinPath(dst, row.Name, dstFs.IsRemote);
                AppServices.Queue.Enqueue(new FileOperationRequest
                {
                    Kind = kind,
                    SrcProvider = srcFs,
                    DstProvider = dstFs,
                    SrcPath = row.FullPath,
                    DstPath = dstPath,
                    DisplayName = row.Name,
                    Overwrite = overwrite,
                    SizeHint = row.IsDirectory ? -1 : (row.SizeBytes ?? -1),
                    OnComplete = refresh,
                });
                enqueued++;
            }

            StatusText = $"Queued {enqueued} {verb}{(enqueued == 1 ? "" : "s")}";
            return Task.CompletedTask;
        }

        private static string JoinPath(string parent, string name, bool isRemote)
        {
            if (!isRemote) return Path.Combine(parent, name);
            var trimmed = parent.EndsWith("/") ? parent : parent + "/";
            return trimmed + name;
        }

        // Pre-scans the destination for items that already exist with the same name
        // and asks the user how to handle them via OverwriteResolver. Returns
        // true if the op should proceed; on Skip, mutates `selection` to drop
        // the conflicting items.
        private bool ResolveOverwrites(
            ref List<FileRowViewModel> selection,
            string destinationDir,
            string verb,
            out bool overwrite)
        {
            overwrite = false;

            var conflicts = selection
                .Where(r => File.Exists(Path.Combine(destinationDir, r.Name))
                         || Directory.Exists(Path.Combine(destinationDir, r.Name)))
                .ToList();

            if (conflicts.Count == 0) return true;

            var resolver = OverwriteResolver;
            var choice = resolver != null
                ? resolver(conflicts.Select(r => r.Name).ToList())
                : OverwriteResolution.Cancel;

            switch (choice)
            {
                case OverwriteResolution.Replace:
                    overwrite = true;
                    return true;
                case OverwriteResolution.Skip:
                    selection = selection.Except(conflicts).ToList();
                    return true;
                default:
                    StatusText = $"{char.ToUpperInvariant(verb[0]) + verb.Substring(1)} cancelled";
                    return false;
            }
        }

        private async Task NewFolderAsync()
        {
            if (ActivePane == null) return;
            var parent = ActivePane.CurrentPath;
            if (string.IsNullOrEmpty(parent))
            {
                StatusText = "New folder: pane has no path";
                return;
            }

            var name = "New folder";
            if (!ActivePane.IsRemote)
            {
                var i = 2;
                while (Directory.Exists(Path.Combine(parent, name)) || File.Exists(Path.Combine(parent, name)))
                {
                    name = $"New folder ({i++})";
                    if (i > 999) break;
                }
            }

            var result = await ActivePane.FileSystem.CreateDirectoryAsync(parent, name);
            if (!result.Success)
            {
                StatusText = $"New folder failed: {result.Error}";
                AppServices.Toast.Error($"Couldn't create folder: {result.Error}");
                return;
            }

            await ActivePane.List.RefreshAsync();
            StatusText = $"Created '{name}'";
            AppServices.Toast.Success($"Created folder '{name}'");
        }

        private async Task NewFileAsync()
        {
            if (ActivePane == null) return;
            var parent = ActivePane.CurrentPath;
            if (string.IsNullOrEmpty(parent))
            {
                StatusText = "New file: pane has no path";
                return;
            }

            var name = "New file.txt";
            string fullPath = Join(parent, name, ActivePane.IsRemote);

            // Pre-check collisions only for local paths — a remote round-trip
            // for every candidate would be wasteful, and OpenWriteAsync with
            // overwrite=false will fail loudly there if the name is taken.
            if (!ActivePane.IsRemote)
            {
                var i = 2;
                while (File.Exists(fullPath) || Directory.Exists(fullPath))
                {
                    name = $"New file ({i++}).txt";
                    fullPath = Join(parent, name, isRemote: false);
                    if (i > 999) break;
                }
            }

            try
            {
                using var stream = await ActivePane.FileSystem.OpenWriteAsync(fullPath, overwrite: false);
            }
            catch (Exception ex)
            {
                Log.Warn("Shell", $"New file failed for {fullPath}", ex);
                StatusText = $"New file failed: {ex.Message}";
                AppServices.Toast.Error($"Couldn't create file: {ex.Message}");
                return;
            }

            await ActivePane.List.RefreshAsync();

            // Background priority lets the refresh-induced layout pass realise
            // the new row's container before IsEditing flips — otherwise the
            // rename TextBox isn't in the tree for IsVisibleChanged to fire on.
            var capturedName = name;
            var capturedPane = ActivePane;
            _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var freshRow = capturedPane.List.Rows
                    .FirstOrDefault(r => string.Equals(r.Name, capturedName, StringComparison.Ordinal));
                if (freshRow == null) return;

                foreach (var s in capturedPane.List.SelectedRows.ToList())
                    s.IsSelected = false;
                freshRow.IsSelected = true;
                capturedPane.List.StartRename(freshRow);
            }), System.Windows.Threading.DispatcherPriority.Background);

            StatusText = $"Created '{name}'";
            AppServices.Toast.Success($"Created file '{name}'");
        }

        private static string Join(string parent, string name, bool isRemote)
        {
            if (isRemote)
            {
                var trimmed = parent.TrimEnd('/');
                return trimmed.Length == 0 ? "/" + name : trimmed + "/" + name;
            }
            return Path.Combine(parent, name);
        }

        private Task RenameAsync()
        {
            if (ActivePane == null) return Task.CompletedTask;
            if (ActivePane.CurrentMode != ViewMode.List)
            {
                StatusText = "Rename: switch to List view first";
                return Task.CompletedTask;
            }

            var nonParentSelected = ActivePane.List.SelectedRows
                .Where(r => !r.IsParentLink)
                .ToList();

            if (nonParentSelected.Count == 0)
            {
                StatusText = "Rename: select an item first";
                return Task.CompletedTask;
            }
            if (nonParentSelected.Count > 1)
            {
                StatusText = "Rename: select exactly one item (multi-rename is Phase 3)";
                return Task.CompletedTask;
            }

            ActivePane.List.StartRename(nonParentSelected[0]);
            StatusText = "Renaming…";
            return Task.CompletedTask;
        }

        private async Task DeleteSelectedAsync(bool toRecycle)
        {
            if (ActivePane == null) return;
            var selection = SnapshotSelection();
            if (selection.Count == 0) return;

            if (!toRecycle && AppServices.Settings.ConfirmDeletePermanent)
            {
                var prompt = selection.Count == 1
                    ? $"Permanently delete '{selection[0].Name}'?\n\nThis can't be undone."
                    : $"Permanently delete {selection.Count} items?\n\nThis can't be undone.";

                var result = MessageBox.Show(
                    prompt,
                    "Confirm permanent delete",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning,
                    MessageBoxResult.Cancel);

                if (result != MessageBoxResult.OK)
                {
                    StatusText = "Delete cancelled";
                    return;
                }
            }

            var fs = ActivePane.FileSystem;
            int ok = 0, fail = 0;
            string? lastError = null;
            foreach (var row in selection)
            {
                StatusText = (toRecycle ? "Recycling " : "Deleting ") + row.Name + "…";
                var result = await fs.DeleteAsync(row.FullPath, toRecycle && !fs.IsRemote);
                if (result.Success) ok++;
                else { fail++; lastError = result.Error; Log.Warn("Shell", $"Delete failed: {row.Name}: {result.Error}"); }
            }

            await ActivePane.List.RefreshAsync();
            var verb = toRecycle && !fs.IsRemote ? "Moved to Recycle Bin" : "Deleted";
            ReportOpOutcome(verb, ok, fail, lastError);
        }

        private void ReportOpOutcome(string verb, int ok, int fail, string? lastError)
        {
            if (fail == 0)
            {
                var brief = $"{verb}: {ok} item{(ok == 1 ? "" : "s")}";
                StatusText = brief;
                if (ok > 0) AppServices.Toast.Success(brief);
            }
            else if (ok == 0)
            {
                StatusText = $"{verb} failed";
                AppServices.Toast.Error($"{verb} failed: {lastError ?? "see log"}");
            }
            else
            {
                var brief = $"{verb}: {ok} ok, {fail} failed";
                StatusText = brief;
                AppServices.Toast.Warning($"{brief}. Last error: {lastError ?? "see log"}");
            }
        }

        private bool HasAnySelection()
        {
            if (ActivePane == null) return false;
            if (ActivePane.CurrentMode == ViewMode.DiskUsage)
                return ActivePane.DiskUsageSelection.Count > 0;
            if (ActivePane.CurrentMode != ViewMode.List) return false;
            return ActivePane.List.SelectedRows.Any(r => !r.IsParentLink);
        }

        private bool HasSingleFileOrDirSelection()
        {
            if (ActivePane == null) return false;
            if (ActivePane.CurrentMode == ViewMode.DiskUsage)
                return ActivePane.DiskUsageSelection.Count == 1;
            if (ActivePane.CurrentMode != ViewMode.List) return false;
            return ActivePane.List.SelectedRows.Count(r => !r.IsParentLink) == 1;
        }

        // Snapshot the selection so view-side mutation during the op doesn't
        // shift what we iterate.
        private List<FileRowViewModel> SnapshotSelection()
        {
            if (ActivePane == null) return new List<FileRowViewModel>();

            if (ActivePane.CurrentMode == ViewMode.DiskUsage)
            {
                var rows = new List<FileRowViewModel>(ActivePane.DiskUsageSelection.Count);
                foreach (var node in ActivePane.DiskUsageSelection)
                {
                    var row = FileRowViewModel.FromDiskUsageNode(node);
                    if (row != null) rows.Add(row);
                }
                return rows;
            }

            return ActivePane.List.SelectedRows
                .Where(r => !r.IsParentLink)
                .ToList();
        }
    }
}
