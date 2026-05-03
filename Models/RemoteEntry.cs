using System;

namespace Josha.Models
{
    internal sealed class RemoteEntry
    {
        public string Name { get; init; } = "";
        public long Size { get; init; }
        public DateTime? ModifiedUtc { get; init; }
        public bool IsDirectory { get; init; }
        public bool IsSymlink { get; init; }
        public string? LinkTarget { get; init; }
        public string? RawPermissions { get; init; }
        public string? Owner { get; init; }
        public string? Group { get; init; }
    }
}
