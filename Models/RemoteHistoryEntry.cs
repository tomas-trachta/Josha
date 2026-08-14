using System.Text.Json.Serialization;

namespace Josha.Models
{
    internal sealed class RemoteHistoryEntry
    {
        public Guid SiteId { get; set; }
        public string SiteName { get; set; } = "";
        public string RemotePath { get; set; } = "";
        public DateTime LastVisitedUtc { get; set; }
        public int VisitCount { get; set; }

        // Leaf folder name for display, same convention as NavigationHistoryEntry.
        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                var trimmed = RemotePath.TrimEnd('/', '\\');
                var i = trimmed.LastIndexOfAny(['/', '\\']);
                var leaf = i >= 0 ? trimmed[(i + 1)..] : trimmed;
                return string.IsNullOrEmpty(leaf) ? RemotePath : leaf;
            }
        }

        [JsonIgnore]
        public string SiteAndPath => $"{SiteName} — {RemotePath}";
    }
}
