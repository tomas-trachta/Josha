using Josha.Business;
using Josha.Business.Git;
using Josha.Models.Git;
using Josha.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;

namespace Josha.ViewModels
{
    internal sealed class GitHistoryWindowViewModel : BaseViewModel
    {
        private readonly string _repoRoot;

        private GitBranch? _selectedBranch;
        private GitCommit? _selectedCommit;
        private GitFileChange? _selectedFile;
        private bool _isLoadingLog;
        private bool _isLoadingDiff;
        private bool _showFullFile;
        private string? _diffMessage;
        private string _statusText = "";
        private string _searchText = "";

        public string RepoRoot => _repoRoot;

        public ObservableCollection<GitBranch> Branches { get; } = new();
        public ObservableCollection<GitCommit> Commits { get; } = new();
        public ObservableCollection<GitFileChange> ChangedFiles { get; } = new();
        public ObservableCollection<GitDiffRow> DiffRows { get; } = new();

        // Client-side filter over the loaded commit page — this is a local
        // review tool, not a full repo search, so it only reaches as far back
        // as GitComponent.LogPageSize already pulled in.
        public ICollectionView CommitsView { get; }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value;
                OnPropertyChanged();
                CommitsView.Refresh();
            }
        }

        public GitBranch? SelectedBranch
        {
            get => _selectedBranch;
            set
            {
                if (_selectedBranch == value) return;
                _selectedBranch = value;
                OnPropertyChanged();
                _ = LoadLogAsync();
            }
        }

        public GitCommit? SelectedCommit
        {
            get => _selectedCommit;
            set
            {
                if (_selectedCommit == value) return;
                _selectedCommit = value;
                OnPropertyChanged();
                _ = LoadCommitFilesAsync();
            }
        }

        public GitFileChange? SelectedFile
        {
            get => _selectedFile;
            set
            {
                if (_selectedFile == value) return;
                _selectedFile = value;
                OnPropertyChanged();
                _ = LoadDiffAsync();
            }
        }

        public bool IsLoadingLog
        {
            get => _isLoadingLog;
            private set { _isLoadingLog = value; OnPropertyChanged(); }
        }

        public bool IsLoadingDiff
        {
            get => _isLoadingDiff;
            private set { _isLoadingDiff = value; OnPropertyChanged(); }
        }

        // Toggles between the usual few-lines-of-context diff and the whole
        // file with the same changes highlighted inline.
        public bool ShowFullFile
        {
            get => _showFullFile;
            set
            {
                if (_showFullFile == value) return;
                _showFullFile = value;
                OnPropertyChanged();
                _ = LoadDiffAsync();
            }
        }

        public string? DiffMessage
        {
            get => _diffMessage;
            private set { _diffMessage = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(); }
        }

        public ICommand RefreshCommand { get; }
        public ICommand OpenFileCommand { get; }

        public GitHistoryWindowViewModel(string repoRoot)
        {
            _repoRoot = repoRoot;
            RefreshCommand = new RelayCommand(_ => _ = LoadAllAsync());
            OpenFileCommand = new RelayCommand(_ => _ = OpenSelectedFileAsync(), _ => _selectedFile != null);

            CommitsView = CollectionViewSource.GetDefaultView(Commits);
            CommitsView.Filter = MatchesSearch;
        }

        internal Task InitializeAsync() => LoadAllAsync();

        private bool MatchesSearch(object obj)
        {
            if (string.IsNullOrWhiteSpace(_searchText)) return true;
            if (obj is not GitCommit commit) return false;

            // Hash already contains ShortHash as a prefix, so one check covers both.
            return commit.Hash.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                || commit.Subject.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
        }

        private async Task LoadAllAsync()
        {
            StatusText = "Loading branches…";
            var branches = await Task.Run(() => GitComponent.GetBranches(_repoRoot));

            Branches.Clear();
            foreach (var branch in branches.OrderByDescending(b => b.IsCurrent).ThenBy(b => b.Name))
                Branches.Add(branch);

            _selectedBranch = Branches.FirstOrDefault(b => b.IsCurrent) ?? Branches.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedBranch));

            await LoadLogAsync();
        }

        private async Task LoadLogAsync()
        {
            IsLoadingLog = true;
            StatusText = "Loading commits…";
            try
            {
                var branchName = _selectedBranch?.Name;
                var commits = await Task.Run(() => GitComponent.GetLog(_repoRoot, branchName));

                Commits.Clear();
                foreach (var commit in commits) Commits.Add(commit);

                SelectedCommit = Commits.FirstOrDefault();
                StatusText = $"{Commits.Count} commit(s) on {_selectedBranch?.DisplayName ?? "(all)"}";
            }
            finally
            {
                IsLoadingLog = false;
            }
        }

        private async Task LoadCommitFilesAsync()
        {
            ChangedFiles.Clear();
            DiffRows.Clear();
            DiffMessage = null;
            if (_selectedCommit == null) return;

            var hash = _selectedCommit.Hash;
            var files = await Task.Run(() => GitComponent.GetCommitFiles(_repoRoot, hash));

            foreach (var file in files) ChangedFiles.Add(file);
            SelectedFile = ChangedFiles.FirstOrDefault();
        }

        private async Task LoadDiffAsync()
        {
            DiffRows.Clear();
            DiffMessage = null;
            if (_selectedCommit == null || _selectedFile == null) return;

            IsLoadingDiff = true;
            try
            {
                var hash = _selectedCommit.Hash;
                var path = _selectedFile.Path;
                var fullFile = _showFullFile;
                var raw = await Task.Run(() => GitComponent.GetRawDiff(_repoRoot, hash, path, fullFile));
                var rows = DiffParser.ParseUnifiedDiff(raw);

                if (rows.Count == 0)
                {
                    DiffMessage = raw.Contains("Binary files")
                        ? "Binary file — diff not shown."
                        : "No textual changes (rename or mode change only).";
                    return;
                }

                foreach (var row in rows) DiffRows.Add(row);
            }
            finally
            {
                IsLoadingDiff = false;
            }
        }

        // Deleted files no longer exist at their own commit, so pull the last
        // surviving version from the first parent instead. Returns null (and
        // toasts) when extraction isn't possible.
        private async Task<string?> ExtractSelectedFileAsync()
        {
            if (_selectedCommit == null || _selectedFile == null) return null;

            if (_selectedFile.ChangeType == GitChangeType.Deleted && _selectedCommit.ParentHashes.Count == 0)
            {
                AppServices.Toast.Warning("This file was deleted with no parent commit to open.");
                return null;
            }

            var hash = _selectedFile.ChangeType == GitChangeType.Deleted
                ? _selectedCommit.ParentHashes[0]
                : _selectedCommit.Hash;
            var path = _selectedFile.Path;
            var repoRoot = _repoRoot;

            var tempPath = await Task.Run(() => GitComponent.ExtractBlobToTempFile(repoRoot, hash, path));
            if (tempPath == null)
                AppServices.Toast.Error("Couldn't extract that file from git.");

            return tempPath;
        }

        private async Task OpenSelectedFileAsync()
        {
            var path = _selectedFile?.Path;
            var tempPath = await ExtractSelectedFileAsync();
            if (tempPath == null) return;

            try
            {
                Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppServices.Toast.Error($"Couldn't open file: {ex.Message}");
            }

            OpenCurrentVersion(path);
        }

        // Handlers come from SHAssocEnumHandlers — the same "recommended
        // apps" list Explorer's own "Open with" menu shows for this extension.
        internal List<OpenWithHandlerEntry> GetOpenWithHandlers()
        {
            if (_selectedFile == null) return new List<OpenWithHandlerEntry>();
            return OpenWithComponent.GetHandlers(System.IO.Path.GetExtension(_selectedFile.Path));
        }

        // Passes both files to the chosen handler in one call, so editors
        // that reuse a running window (VS Code, Sublime, ...) open them as
        // two tabs in a single instance instead of two separate windows.
        internal async Task OpenSelectedFileWithHandlerAsync(OpenWithHandlerEntry handler)
        {
            var path = _selectedFile?.Path;
            var tempPath = await ExtractSelectedFileAsync();
            if (tempPath == null) return;

            var paths = new List<string> { tempPath };
            var workingPath = System.IO.Path.Combine(_repoRoot, path!.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(workingPath)) paths.Add(workingPath);

            if (!OpenWithComponent.Launch(handler.Handler, paths))
                AppServices.Toast.Error($"Couldn't open with {handler.DisplayName}.");
        }

        // Opens the current on-disk version (if it still exists) in its
        // default app so the historical version just opened can be compared
        // against it side by side.
        private void OpenCurrentVersion(string? relativePath)
        {
            if (relativePath == null) return;

            var workingPath = System.IO.Path.Combine(_repoRoot, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(workingPath)) return;

            try
            {
                Process.Start(new ProcessStartInfo(workingPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppServices.Toast.Error($"Couldn't open current file: {ex.Message}");
            }
        }
    }
}
