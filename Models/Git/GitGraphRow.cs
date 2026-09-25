using System.Collections.Generic;

namespace Josha.Models.Git
{
    internal readonly record struct GitGraphLink(int Lane, int ColorIndex);

    internal sealed class GitGraphRow
    {
        public int NodeLane { get; init; }
        public int ColorIndex { get; init; }
        public List<GitGraphLink> Incoming { get; } = new();
        public List<GitGraphLink> Outgoing { get; } = new();
        public List<GitGraphLink> PassThrough { get; } = new();
    }
}
