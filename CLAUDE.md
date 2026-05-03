# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
dotnet build Josha.sln
dotnet run --project Josha.csproj
```

Target framework: .NET 9 (`net9.0-windows`), WPF desktop application. ServerGC + ConcurrentGC enabled for the parallel disk scanner. No test project.

NuGet dependencies:
- **FluentFTP** — FTP / FTPS (explicit AUTH TLS, implicit on 990), passive/active, fingerprint pinning.
- **SSH.NET** — SFTP client.
- **MahApps.Metro.IconPacks.Material** — Material Design icons.
- **Vanara.PInvoke.Shell32** — Win32 `IContextMenu` invocation for the native shell menu.

## Architecture

Josha is a **Norton/Total Commander-style dual-pane file manager** for Windows with integrated FTP/SFTP, an embedded disk-usage analyzer, command palette, queued file operations, and theming. Each pane is a column of tabs; each tab is a `FilePaneViewModel` that targets either the local filesystem or a remote site via the `IFileSystemProvider` abstraction.

**Startup flow:** `App.xaml.cs` → `MainWindow.xaml` (custom WindowChrome, no native chrome) → `AppShellViewModel` constructs a `LeftColumn` and `RightColumn` (`PaneColumnViewModel`), each starting with a single tab on `C:\`. `AppServices.Initialize()` wires up settings, theme, toast service, file-op queue, and snapshot service before the shell becomes interactive.

**Three view modes per tab** (`Models/ViewMode.cs`, switched by Ctrl+1/2/3):
1. **List** (default) — sortable columnar `FileListView`.
2. **Tiles** — icon grid `FileTilesView`.
3. **DiskUsage** — the canvas-based `TreeGraphControl` analyzer (disabled on remote tabs; remotes have no snapshot).

**Function keys** drive file operations: F2 Rename, F3 View, F4 Edit, F5 Copy, F6 Move, F7 Mkdir, F8 Delete (Shift+F8 permanent). All ops route through `FileOperationQueue` (3 concurrent workers, retry on failure, queue pill in status bar).

### Business Layer (`Business/`)

#### Scanning & disk-usage
- **ScanCore.cs** — Central scan orchestrator. `DeepScan()` populates a `DirOD` tree (parallel for first 2 levels, sequential below). `Reconcile()` re-scans only changed paths. Uses Win32 `SetThreadPriority(THREAD_MODE_BACKGROUND_BEGIN)` for low I/O priority.
- **DirectoryAnalyserComponent.cs** — Public façades: `RunShallow()` (root dirs only, instant browsing), `Run()` (full parallel), `DeepScanSequential()` (cancellable, low priority). Delegates to `ScanCore`.
- **FileAnalyserComponent.cs** — Per-directory file enumeration plus static `ReadFile`/`WriteFile`/`FileExists` helpers used by persistence.
- **SearchComponent.cs** — Case-insensitive name search over a `DirOD` tree, capped at 50 results. Used by the disk-usage view.

#### Filesystem providers
- **IFileSystemProvider.cs** — Abstraction over local/remote: `EnumerateAsync`, `Copy/Move/Rename/CreateDirectory/DeleteAsync`, `OpenRead/OpenWriteAsync`, `ImportFromAsync` (cross-provider transfer hook for local↔remote). Returns `FsEntry` records.
- **LocalFileSystemProvider.cs** — Local adapter wrapping `FileOpsComponent` and `DirectoryInfo`/`FileInfo`. `SupportsTreeGraph = true`.
- **Ftp/RemoteFileSystemProvider.cs** — Remote adapter; leases clients from `RemoteConnectionPool`. `IsRemote = true`, `SupportsTreeGraph = false`.
- **Ftp/IRemoteClient.cs** — `ListAsync`/`DownloadAsync`/`UploadAsync` interface, implemented by both clients.
- **Ftp/FtpClientComponent.cs** — FluentFTP wrapper. Plain FTP, FTPS explicit AUTH TLS, FTPS implicit on 990; passive/active mode; TLS validation modes Strict / AcceptOnFirstUse (SHA-256 fingerprint pinning) / AcceptAny.
- **Ftp/SftpClientComponent.cs** — SSH.NET SFTP wrapper.
- **Ftp/RemoteConnectionPool.cs** — Per-site connection pool (default max 2). `await using var lease = await pool.AcquireAsync(site, ct)` lease pattern. Idle clients recycled at 30s; faulted leases disposed.

#### File operations
- **FileOpsComponent.cs** — Static `Copy/Move/Rename/CreateDirectory/Delete` returning `OpResult`. Same-volume moves use atomic `Directory.Move`; cross-volume goes copy-then-delete. Recycle bin via shell API. 1 MB copy buffer. Notifies `SnapshotComponent` on success.
- **ShellContextMenuComponent.cs** — Invokes Windows shell `IContextMenu` via Vanara at screen coordinates. Only `IContextMenu` (not v2/v3), so owner-drawn items render as text.

#### Persistence (all under `C:\josha_data\`)

All encrypted files use **Windows DPAPI** (`DataProtectionScope.CurrentUser`) with a per-component entropy string (`"Josha/<file>/v1"`) so a different process running as the same user can't unprotect another file by passing null entropy. The OS pins the protection key to user profile + machine — files are unreadable if the disk leaves the device or the profile is copied to another user. On unprotect failure, callers move the file aside as `*.{reason}-{stamp}.bak` so the next save can't overwrite the original.

- **PersistenceFile.cs** — Shared load/save for the string-payload encrypted-flat-file pattern (namespaces, bindings, bookmarks). Wraps DPAPI ciphertext in the `PersistenceMigrator` envelope. Handles `BackupAndIsolate` on unprotect / corruption / newer-version / pre-DPAPI-format.
- **CryptoComponent.cs** — Thin wrapper over `ProtectedData.Protect/Unprotect` with byte and string overloads.
- **SettingsComponent.cs** — Plain JSON at `settings.json` (not encrypted; no secrets). `AppSettings`: editor path, theme, confirm-delete-permanent, default view mode, font scale.
- **BookmarkComponent.cs** — `bookmarks.dans`. DPAPI entropy `"Josha/bookmarks/v1"`. Tab-separated `Name\tTargetPath`.
- **SiteManagerComponent.cs** — `sites.dans`. DPAPI entropy `"Josha/sites/v1"`. JSON-serialized `FtpSite` list. Inlines its own DPAPI calls (predates `PersistenceFile`'s DPAPI conversion); the `BackupAndIsolate`-on-unprotect-failure pattern lives here as well, plus a user-facing toast.
- **SnapshotComponent.cs** — `tree.{letter}.daps` per drive (encrypted text serialization of the `DirOD` tree). DPAPI entropy `"Josha/snapshot/v1"`. `MigrateLegacyOnStartup()` renames the old `tree.daps` to `tree.C.daps`. Raises `SnapshotChanged` on mutation; `SnapshotService` debounces writes by 2s.
- **NamespaceComponent.cs** — Legacy `namespaces.dans` / `bindings.dans` for the old named-tree-namespace + Ctrl+key feature. DPAPI entropy `"Josha/namespaces/v1"` and `"Josha/bindings/v1"`. Persisted but no longer wired into the modern UI.

### Services (`Services/`)

- **AppServices.cs** — Static singleton: `Toast`, `Queue`, `Snapshot`, `Settings`. `Initialize()` wires everything and loads settings. `SettingsChanged` event for live updates.
- **Log.cs** — Category-tagged logging to `C:\josha_data\logs\Josha-YYYY-MM-DD.log`. Info/Warn/Error.
- **FileOperationQueue.cs** — Async multi-worker queue (Channels, default 3 concurrent). Wraps `FileOperationRequest` in `QueueJobViewModel` with status (Pending/Running/Succeeded/Failed/Cancelled) and progress. Terminal jobs persist 10s in UI before auto-clear.
- **ToastService.cs** — Observable toast list (max 6). Severity-driven auto-dismiss (4s default; errors require dismiss). Optional action label/callback.
- **ThemeService.cs** — Loads `Theme.Light.xaml` for Light or System+light-mode; otherwise default dark. Resources keyed by semantic name (`Brush.Surface`, `Brush.Accent`, etc.).
- **SnapshotService.cs** — Cache + lifecycle for per-drive `DirOD` snapshots. `LoadAsync`, `ScanDriveAsync`, `ReconcileAsync`. Coalesces writes via `CancellationTokenSource` swap (2s debounce).
- **EditOnServerWatcher.cs** — F4 on remote files: download → temp file → launch editor → watch the **directory** (not file — editors like VS Code rename-save) for Changed+Renamed (400ms debounce) → SHA-compare to skip no-op saves → upload. App-exit registry cleans up watchers.
- **PersistenceMigrator.cs** — Versioned envelope for `.dans` files: magic `"DAS"` + version + flags + 3 reserved + payload (8-byte header). `Unwrap()` detects legacy (no envelope) and flags for re-wrap on next save.

### Models (`Models/`)

- **ViewMode.cs** — `List | Tiles | DiskUsage`.
- **AppSettings.cs** — Editor path, theme, confirm-delete-permanent, default view mode, font scale.
- **Bookmark.cs** — `(Name, TargetPath)` record.
- **FtpSite.cs** — Id, Name, Host, Port, Username, Password, StartDirectory, `Protocol`, `Mode`, Encoding, `TlsValidation`, PinnedFingerprint, AsciiExtensions, LastUsedUtc.
- **FtpProtocol.cs** — `FtpProtocol` (Ftp/FtpsExplicit/FtpsImplicit/Sftp), `FtpMode` (Passive/Active), `TlsValidation` (Strict/AcceptOnFirstUse/AcceptAny).
- **CommandPaletteItem.cs** — Category, Title, Subtitle, Action, CategoryOrder. Scored prefix > word-start > substring > subsequence.
- **ConnectionStatus.cs** — Remote tab connection state.
- **FileOperationRequest.cs** — Type/source/dest/overwrite for `FileOperationQueue`.
- **OverwriteResolution.cs** — `Replace | Skip | Cancel`. Returned by `OverwriteSheet`.
- **RemoteEntry.cs** — Listing entry from `IRemoteClient.ListAsync`.
- **DirOD.cs / FileOD.cs** — Snapshot tree nodes. `DirOD` holds `Subdirectories[]`, `Files[]`, `SizeKiloBytes`, `IsScanned`. `GetDirSize()` parallel for top 2 levels, sequential below.
- **SearchResult.cs** — Disk-usage search hit with `LocationPath` / `GroupLabel` for grouped UI.
- **DANamespace.cs / DANamespaceBinding.cs** — Legacy namespace + Ctrl+key binding models.

### ViewModels (`ViewModels/`)

- **BaseViewModel.cs** — `INotifyPropertyChanged` base.
- **RelayCommand.cs** — `ICommand` via delegates with `CommandManager.RequerySuggested`.
- **AppShellViewModel.cs** — Root VM bound to `MainWindow`. Owns `LeftColumn`/`RightColumn`, `ActiveColumn`/`ActivePane`, status text, clock. ~30+ commands (file ops, tabs, bookmarks, settings, command palette, FTP connect/quick-connect, paste from clipboard with cut/copy detection via `"Preferred DropEffect"`). Dialog-request events: `BookmarksPickerRequested`, `NewConnectionRequested`, `SiteManagerRequested`, `OverwriteResolver` (`Func`), `PatternPromptRequested`. `QuickConnect()` sorts sites by `LastUsedUtc` for Ctrl+Shift+1–9.
- **PaneColumnViewModel.cs** — One column. `Tabs` (`ObservableCollection<FilePaneViewModel>`), `ActiveTab`, tab commands (new/close/cycle), `AddRemoteTab(FtpSite)`.
- **FilePaneViewModel.cs** — One tab. `CurrentPath`, `CurrentMode`, `IsActive`, `IsRemote`, `FileSystem` (provider), `List` (`FileListViewModel`), lazy `_diskUsageRoot` for the tree mode. Commands: switch view, back/forward, refresh. `NavigateAsync`, `ConnectAsync`, `AttachRemoteSite`, `CancelRemoteListing`.
- **FileListViewModel.cs** — List/tiles model. `Rows` + `RowsView` (`ICollectionView` with sort/filter), `SortColumn`, `FilterText`, `ShowHiddenFiles`, `IsLoading`, `LoadError`. `RefreshAsync` enumerates via the provider.
- **FileRowViewModel.cs** — One row. Name/Extension/FullPath/IsDirectory/SizeBytes/ModifiedUtc, `IsParentLink` (synthetic `..`), `IsSelected`, `IsEditing`, formatted `SizeDisplay`.
- **FilePreviewViewModel.cs** — Quick-preview pane state (text excerpt / binary hex / image / placeholder).
- **CommandPaletteViewModel.cs** — Fuzzy palette. `FilteredItems`, `Query`, `Selected`. Top 80 results.
- **DirectoryTreeItemViewModel.cs / FileTreeItemViewModel.cs** — Disk-usage tree nodes with lazy loading via dummy placeholder. `EnsureScanned()` does on-demand I/O for unscanned dirs.
- **TreeItemViewModel.cs** — Abstract base with shared `FormatSize()`.
- **QueueJobViewModel.cs** — One queued op: request, status, byte progress, retry/cancel commands.
- **SettingsViewModel.cs** — Settings sheet bindings.
- **ToastViewModel.cs** — One toast (text, severity, action, dismiss timer).
- **MainWindowViewModel.cs** — Thin shim retained for compatibility; the real root is `AppShellViewModel`.

### Views (`Views/`)

Shell pieces: **MainWindow.xaml** (custom title bar + dual `FilePaneView` split by `GridSplitter` + `FunctionKeyBar` + `StatusBarControl` + `ToastHost`).

Pane internals:
- **FilePaneView** — Hosts the active view (`FileListView` / `FileTilesView` / `TreeGraphControl`) plus `PathBarControl`, `SearchBox`, `DriveBarControl`, optional `QuickPreviewPane`.
- **FileListView** — Sortable columnar list, multi-select, inline rename.
- **FileTilesView** — Icon-tiles grid.
- **TreeGraphControl** — Custom canvas tree renderer for DiskUsage mode. Viewport virtualization (binary search on flat node list), per-VM visual cache, single `DrawingVisual` for all connector lines, mouse-wheel zoom 0.1×–5×, click-drag pan, scroll/zoom mode toggle (Shift/Ctrl+wheel), `NavigateTo(SearchResult)` for path-expand-and-pan, context menus (Open / Open containing folder / Copy path).
- **PaneTabBar**, **PathBarControl**, **DriveBarControl**, **StatusBarControl**, **FunctionKeyBar**, **SearchBox**, **QuickPreviewPane**, **QueueStatusPill**.

Modal sheets: **BookmarksDialog**, **NewConnectionSheet**, **SiteManagerSheet**, **SettingsSheet**, **OverwriteSheet**, **PatternPromptDialog**, **CommandPalette**, **InternalViewer** (F3 viewer window), **Toasts/ToastHost**.

- **FileIconMap.cs** (in `Views/` and `Business/`) — ~100 file extensions → icon styles (body brush, fold brush, label text). Categories: images, documents, code, web, text, archives, audio, video, executables, data, fonts. All brushes frozen for thread safety.

### Converters (`Converters/`)

`InverseBoolToVisibilityConverter`, `StringNotEmptyToVisibilityConverter`, `ResourceKeyToBrushConverter` (looks up a theme-resource key and returns the brush).

### Other

- **App.xaml.cs** — Global exception handling for dispatcher and unobserved task exceptions.
- **CustomExceptions/DirectoryExceptions.cs** — `RootDirectoryAnalysisException`, `GetDirectoryNameException`.

## Data persistence

All data lives in `C:\josha_data\` (created on first write):

| File | Format | Encryption | Entropy | Purpose |
|------|--------|-----------|---------|---------|
| `settings.json` | JSON | none | — | `AppSettings` |
| `bookmarks.dans` | TSV + envelope | DPAPI | `Josha/bookmarks/v1` | bookmarks |
| `sites.dans` | JSON + envelope | DPAPI | `Josha/sites/v1` | FTP/SFTP sites |
| `tree.{letter}.daps` | text + envelope | DPAPI | `Josha/snapshot/v1` | per-drive `DirOD` snapshot |
| `namespaces.dans` | TSV + envelope | DPAPI | `Josha/namespaces/v1` | legacy namespace feature |
| `bindings.dans` | TSV + envelope | DPAPI | `Josha/bindings/v1` | legacy Ctrl+key feature |
| `logs/Josha-YYYY-MM-DD.log` | text | none | — | category-tagged logs |

- All encryption is **Windows DPAPI** (`DataProtectionScope.CurrentUser`). The OS-managed protection key is pinned to the user profile + machine, so files are unreadable if the disk leaves the device or the profile is copied to another user. There is no passphrase prompt and no hardware-ID logic.
- **Per-file entropy** is mixed into Protect/Unprotect so a different component (or a different process running as the same user) can't unprotect another file by passing null entropy.
- **Failure handling:** on unprotect failure, corruption, or newer-version envelope, the file is moved aside to `<name>.{reason}-{stamp}.bak` (e.g., `sites.dans.dpapi-failed-20260503-101530.bak`). Without this, the next save would silently overwrite the original — the only copy of credentials/bookmarks. The user is toasted for `sites.dans`; other files just log.
- **Envelope:** all `.dans` / `.daps` files are wrapped by `PersistenceMigrator` with magic `"DAS"` + version byte + flags + 3 reserved bytes. Pre-DPAPI files (envelope-less AES-HWID ciphertext) cannot be decrypted and are isolated to `.bak` on first read.

## Key patterns

- **Provider abstraction:** the pane UI is provider-agnostic. Cross-pane operations between local and remote use `ImportFromAsync` so remote→remote or local→remote streams without touching disk twice when avoidable.
- **Parallelism threshold:** scanners and `DirOD.GetDirSize()` use `Parallel.ForEach` only when `level < 2`, sequential below — bounds thread creation during deep recursion.
- **File sizes:** stored in **kilobytes** as `info.Length / 1000` (decimal KB, not 1024-byte KiB).
- **Tree children:** sorted by size descending, directories before files.
- **On-demand scanning:** `RunShallow()` produces unscanned `DirOD`s with a dummy child placeholder; `EnsureScanned()` fills them on expand.
- **Snapshot debouncing:** mutations notify `SnapshotComponent`, which raises an event coalesced by `SnapshotService` with a 2s `CancellationTokenSource` swap so bulk ops don't spam writes.
- **Connection pool leases:** `await using` lease pattern; faulted clients disposed, idle clients recycled at 30s.
- **Edit-on-server:** watch the temp **directory** (not the file) so atomic-rename saves from VS Code / Vim / Notepad++ are caught; SHA-compare before re-upload to skip no-op saves.
- **Command palette:** Ctrl+P; scoring is prefix > word-start > substring > subsequence; top 80.
- **Theming:** brushes are resource-keyed (`Brush.Surface`, `Brush.Accent`, …); custom controls look them up via `ResourceKeyToBrushConverter` so theme switches propagate live.
- **Keyboard:** function keys (F2–F8) drive ops; Ctrl+1/2/3 view modes; Ctrl+Tab / Ctrl+Left/Right cycle tabs/panes; Ctrl+Shift+1–9 quick-connect to most-recent sites; Ctrl+H toggle hidden; Ctrl+F focus filter; Ctrl+V paste (cut/copy detected via `"Preferred DropEffect"` clipboard marker).
