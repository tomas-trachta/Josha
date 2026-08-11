using Josha.Business;
using Josha.Models;
using System.Collections.ObjectModel;
using System.Linq;

namespace Josha.Services
{
    // Global, cross-pane log of visited local directories backing the history
    // sidebar. Recorded only for local-filesystem navigation (see
    // FilePaneViewModel.NavigateInternalAsync) — remote paths aren't
    // meaningful across different sites/sessions. Persisted immediately on
    // every visit; the file is small and visits are infrequent enough that a
    // debounce (like SnapshotService) isn't worth the extra state.
    internal sealed class NavigationHistoryService
    {
        private const int MaxRecentShown = 40;
        private const int MaxMostVisitedShown = 15;
        private const int MaxTracked = 200;

        private readonly List<NavigationHistoryEntry> _entries;

        public ObservableCollection<NavigationHistoryEntry> Recent { get; } = new();
        public ObservableCollection<NavigationHistoryEntry> MostVisited { get; } = new();

        public NavigationHistoryService()
        {
            _entries = NavigationHistoryComponent.Load();
            RefreshViews();
        }

        public void RecordVisit(string path)
        {
            var normalized = path.TrimEnd('\\', '/');
            if (string.IsNullOrEmpty(normalized)) return;

            var existing = _entries.FirstOrDefault(e =>
                string.Equals(e.TargetPath, normalized, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.VisitCount++;
                existing.LastVisitedUtc = DateTime.UtcNow;
            }
            else
            {
                _entries.Add(new NavigationHistoryEntry
                {
                    TargetPath = normalized,
                    VisitCount = 1,
                    LastVisitedUtc = DateTime.UtcNow,
                });
            }

            TrimTracked();
            RefreshViews();
            NavigationHistoryComponent.Save(_entries);
        }

        // Bounds the persisted set so the file doesn't grow forever; drops the
        // stalest entries first, not the least-visited ones, so a folder
        // visited heavily long ago doesn't get evicted ahead of clutter from
        // yesterday.
        private void TrimTracked()
        {
            if (_entries.Count <= MaxTracked) return;

            var stalest = _entries
                .OrderBy(e => e.LastVisitedUtc)
                .Take(_entries.Count - MaxTracked)
                .ToList();

            foreach (var entry in stalest)
                _entries.Remove(entry);
        }

        private void RefreshViews()
        {
            Recent.Clear();
            foreach (var entry in _entries.OrderByDescending(e => e.LastVisitedUtc).Take(MaxRecentShown))
                Recent.Add(entry);

            MostVisited.Clear();
            foreach (var entry in _entries.OrderByDescending(e => e.VisitCount).ThenByDescending(e => e.LastVisitedUtc).Take(MaxMostVisitedShown))
                MostVisited.Add(entry);
        }
    }
}
