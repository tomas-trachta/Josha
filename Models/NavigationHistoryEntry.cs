using System.IO;
using System.Text.Json.Serialization;

namespace Josha.Models
{
    internal sealed class NavigationHistoryEntry
    {
        public string TargetPath { get; set; } = "";
        public DateTime LastVisitedUtc { get; set; }
        public int VisitCount { get; set; }

        // Leaf folder name for display — the sidebar shows this instead of
        // the full path (which is still available via tooltip). Falls back
        // to the full path for drive roots ("C:\", UNC shares) that have no
        // leaf segment.
        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                var trimmed = TargetPath.TrimEnd('\\', '/');
                var leaf = Path.GetFileName(trimmed);
                return string.IsNullOrEmpty(leaf) ? TargetPath : leaf;
            }
        }
    }
}
