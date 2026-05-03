using Josha.Business;
using Josha.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Josha.ViewModels
{
    internal class DirectoryTreeItemViewModel : TreeItemViewModel
    {
        private static readonly DirectoryTreeItemViewModel DummyChild = new();

        private readonly DirOD? _directory;
        private readonly int _depth;
        private bool _isExpanded;
        private bool _preloaded;

        internal DirOD? Model => _directory;

        public override ObservableCollection<TreeItemViewModel> Children { get; } = [];

        public override string DisplayName => _directory?.Name ?? "";
        public override string SizeDisplay => _directory != null ? FormatSize(_directory.SizeKiloBytes) : "";
        public int FileCount => _directory?.Files.Length ?? 0;
        public int SubdirectoryCount => _directory?.Subdirectories.Length ?? 0;
        public override bool HasContent =>
            (_directory != null && !_directory.IsScanned) || SubdirectoryCount > 0 || FileCount > 0;

        // Dummy constructor for placeholder node
        private DirectoryTreeItemViewModel()
        {
            _directory = null;
            _depth = -1;
        }

        public DirectoryTreeItemViewModel(DirOD directory, int depth)
        {
            _directory = directory;
            _depth = depth;

            if (depth < 1)
            {
                LoadChildren();
                _isExpanded = true;
            }
            else if (HasContent)
            {
                Children.Add(DummyChild);
            }
        }

        public override bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                _preloaded = false;
                OnPropertyChanged();

                if (_isExpanded && Children.Count == 1 && Children[0] == DummyChild)
                {
                    EnsureScanned();
                    Children.Clear();
                    LoadChildren();
                }

                // After expanding, silently verify this directory's immediate level
                // against disk. If anything changed since the last reconcile, refresh
                // the children. mtime probe is one syscall; usually returns
                // immediately with no work.
                if (_isExpanded && _directory != null && _directory.IsScanned)
                {
                    _ = Task.Run(SilentReconcile);
                }
            }
        }

        private void SilentReconcile()
        {
            var dir = _directory;
            if (dir == null) return;

            // Snapshot before off-dispatcher I/O — apply aborts if a file op or
            // another reconcile mutated this dir while we were enumerating.
            var mutationVersionAtRead = dir.MutationVersion;

            // Fast path: parent mtime unchanged → nothing was added/removed/renamed
            // at this level since the last full reconcile.
            if (dir.LastWriteTimeUtcTicks != 0 &&
                ScanCore.TryGetDirMtimeTicks(dir.Path, out var actualTicks) &&
                actualTicks == dir.LastWriteTimeUtcTicks)
            {
                return;
            }

            DirectoryInfo dirInfo;
            try { dirInfo = new DirectoryInfo(dir.Path); }
            catch { return; }

            var subInfos = ScanCore.SafeGetDirectoryInfos(dirInfo);
            var fileInfos = ScanCore.SafeGetFileInfos(dirInfo);

            var existingByName = new Dictionary<string, DirOD>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in dir.Subdirectories)
                if (!existingByName.ContainsKey(s.Name))
                    existingByName[s.Name] = s;

            var newSubs = new DirOD[subInfos.Length];
            for (int i = 0; i < subInfos.Length; i++)
            {
                var di = subInfos[i];
                if (existingByName.TryGetValue(di.Name, out var existing))
                {
                    existing.Path = di.FullName;
                    newSubs[i] = existing;
                }
                else
                {
                    // New on disk — leave IsScanned=false so its contents are
                    // populated lazily when the user expands it.
                    newSubs[i] = new DirOD(di.Name, di.FullName)
                    {
                        IsScanned = false,
                        LastWriteTimeUtcTicks = ScanCore.SafeMtime(di),
                    };
                }
            }

            var newFiles = new FileOD[fileInfos.Length];
            for (int i = 0; i < fileInfos.Length; i++)
            {
                long len;
                try { len = fileInfos[i].Length; } catch { len = 0; }
                newFiles[i] = new FileOD(fileInfos[i].Name, len / 1000);
            }

            var freshMtime = ScanCore.SafeMtime(dirInfo);

            bool hasChanges = DetectStructuralChange(dir, newSubs, newFiles);

            // Apply on UI thread so concurrent UI iteration sees a consistent
            // before/after, and the ObservableCollection mutation runs on the
            // dispatcher.
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            dispatcher.InvokeAsync(() =>
            {
                if (!_isExpanded || _directory == null) return;

                // Aborting drops this pass — next mtime change or expand re-triggers.
                if (_directory.MutationVersion != mutationVersionAtRead)
                    return;

                _directory.Subdirectories = newSubs;
                _directory.Files = newFiles;
                _directory.LastWriteTimeUtcTicks = freshMtime;

                decimal size = 0;
                foreach (var f in _directory.Files) size += f.SizeKiloBytes;
                foreach (var s in _directory.Subdirectories) size += s.SizeKiloBytes;
                _directory.SizeKiloBytes = size;

                _directory.BumpMutationVersion();

                MergeChildrenFromModel();

                if (hasChanges)
                    SnapshotComponent.NotifySnapshotChanged();
            });
        }

        // True when the freshly-read children differ from the cached children at
        // this level — counts, names, or per-file sizes. Sub-mtimes aren't
        // considered: deeper changes are caught by the next per-folder reconcile.
        private static bool DetectStructuralChange(DirOD dir, DirOD[] newSubs, FileOD[] newFiles)
        {
            if (newSubs.Length != dir.Subdirectories.Length) return true;
            if (newFiles.Length != dir.Files.Length) return true;

            var subNames = new HashSet<string>(dir.Subdirectories.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var s in dir.Subdirectories) subNames.Add(s.Name);
            foreach (var s in newSubs)
                if (!subNames.Contains(s.Name)) return true;

            var fileSizes = new Dictionary<string, decimal>(dir.Files.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var f in dir.Files) fileSizes[f.Name] = f.SizeKiloBytes;
            foreach (var f in newFiles)
            {
                if (!fileSizes.TryGetValue(f.Name, out var oldSize)) return true;
                if (oldSize != f.SizeKiloBytes) return true;
            }

            return false;
        }

        // Rebuilds Children from the current DirOD state, reusing existing VMs
        // whose Model reference is still present. This preserves expansion state
        // for unchanged grandchildren when something at this level changed.
        private void MergeChildrenFromModel()
        {
            if (_directory == null) return;

            var dirVms = new Dictionary<DirOD, DirectoryTreeItemViewModel>();
            var fileVms = new Dictionary<FileOD, FileTreeItemViewModel>();
            foreach (var child in Children)
            {
                if (child is DirectoryTreeItemViewModel dvm && dvm.Model != null)
                    dirVms[dvm.Model] = dvm;
                else if (child is FileTreeItemViewModel fvm)
                    fileVms[fvm.Model] = fvm;
            }

            Children.Clear();

            foreach (var subdir in _directory.Subdirectories.OrderByDescending(d => d.SizeKiloBytes))
            {
                if (dirVms.TryGetValue(subdir, out var existing))
                    Children.Add(existing);
                else
                    Children.Add(new DirectoryTreeItemViewModel(subdir, _depth + 1));
            }

            foreach (var file in _directory.Files.OrderByDescending(f => f.SizeKiloBytes))
            {
                if (fileVms.TryGetValue(file, out var existing))
                    Children.Add(existing);
                else
                    Children.Add(new FileTreeItemViewModel(file, _directory.Path));
            }
        }

        public override void PreloadChildren()
        {
            if (_preloaded || _isExpanded || _directory == null) return;
            EnsureScanned();
            if (Children.Count == 1 && Children[0] == DummyChild)
            {
                Children.Clear();
                LoadChildren();
                _preloaded = true;
            }
        }

        public override void FlushPreloadedChildren()
        {
            if (!_preloaded || _isExpanded) return;
            Children.Clear();
            if (HasContent)
                Children.Add(DummyChild);
            _preloaded = false;
        }

        private void EnsureScanned()
        {
            if (_directory == null || _directory.IsScanned) return;

            try
            {
                var di = new DirectoryInfo(_directory.Path);
                var subInfos = di.GetDirectories();
                var subdirs = new DirOD[subInfos.Length];
                for (int i = 0; i < subInfos.Length; i++)
                {
                    var info = subInfos[i];
                    subdirs[i] = new DirOD(info.Name, info.FullName)
                    {
                        LastWriteTimeUtcTicks = SafeMtime(info),
                    };
                }
                _directory.Subdirectories = subdirs;

                var fileInfos = di.GetFiles();
                var files = new FileOD[fileInfos.Length];
                for (int i = 0; i < fileInfos.Length; i++)
                {
                    long len;
                    try { len = fileInfos[i].Length; } catch { len = 0; }
                    files[i] = new FileOD(fileInfos[i].Name, len / 1000);
                }
                _directory.Files = files;

                if (_directory.LastWriteTimeUtcTicks == 0)
                    _directory.LastWriteTimeUtcTicks = SafeMtime(di);
            }
            catch
            {
                _directory.Subdirectories = [];
                _directory.Files = [];
            }

            _directory.SizeKiloBytes = _directory.Files.Sum(f => f.SizeKiloBytes);
            _directory.IsScanned = true;
        }

        private static long SafeMtime(System.IO.FileSystemInfo info)
        {
            try
            {
                var t = info.LastWriteTimeUtc;
                if (t.Year < 1700) return 0;
                return t.Ticks;
            }
            catch { return 0; }
        }

        private void LoadChildren()
        {
            if (_directory == null) return;

            foreach (var subdir in _directory.Subdirectories.OrderByDescending(d => d.SizeKiloBytes))
            {
                Children.Add(new DirectoryTreeItemViewModel(subdir, _depth + 1));
            }

            foreach (var file in _directory.Files.OrderByDescending(f => f.SizeKiloBytes))
            {
                Children.Add(new FileTreeItemViewModel(file, _directory.Path));
            }
        }
    }
}
