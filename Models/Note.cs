using System.IO;
using System.Text.Json.Serialization;

namespace Josha.Models
{
    internal sealed class Note
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string TargetPath { get; set; } = "";
        public bool IsDirectory { get; set; }
        public string Text { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
        public DateTime ModifiedUtc { get; set; }

        // Leaf name for display, same convention as NavigationHistoryEntry.
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

        // Single-line preview for the sidebar row — full text opens in the editor.
        [JsonIgnore]
        public string Snippet
        {
            get
            {
                var oneLine = Text.Replace('\r', ' ').Replace('\n', ' ').Trim();
                return oneLine.Length > 80 ? oneLine[..80] + "…" : oneLine;
            }
        }
    }
}
