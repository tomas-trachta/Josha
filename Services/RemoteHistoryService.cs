using Josha.Business;
using Josha.Models;
using System.Collections.ObjectModel;
using System.Linq;

namespace Josha.Services
{
    // Global, cross-pane log of visited remote directories backing the
    // "Remote directories" sidebar section. Recorded only for remote
    // navigation (see FilePaneViewModel.NavigateInternalAsync); entries are
    // keyed by (SiteId, path) since the same path can exist on different
    // sites. Persisted immediately on every visit, same rationale as
    // NavigationHistoryService.
    internal sealed class RemoteHistoryService
    {
        private const int MaxRecentShown = 40;
        private const int MaxTracked = 200;

        private readonly List<RemoteHistoryEntry> _entries;

        public ObservableCollection<RemoteHistoryEntry> Recent { get; } = new();

        public RemoteHistoryService()
        {
            _entries = RemoteHistoryComponent.Load();
            RefreshViews();
        }

        public void RecordVisit(Guid siteId, string siteName, string path)
        {
            var normalized = path.TrimEnd('/', '\\');
            if (string.IsNullOrEmpty(normalized)) normalized = "/";

            var existing = _entries.FirstOrDefault(e =>
                e.SiteId == siteId &&
                string.Equals(e.RemotePath, normalized, StringComparison.Ordinal));

            if (existing != null)
            {
                existing.VisitCount++;
                existing.LastVisitedUtc = DateTime.UtcNow;
                existing.SiteName = siteName;
            }
            else
            {
                _entries.Add(new RemoteHistoryEntry
                {
                    SiteId = siteId,
                    SiteName = siteName,
                    RemotePath = normalized,
                    VisitCount = 1,
                    LastVisitedUtc = DateTime.UtcNow,
                });
            }

            TrimTracked();
            RefreshViews();
            RemoteHistoryComponent.Save(_entries);
        }

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
        }
    }
}
