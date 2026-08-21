using System;
using System.Collections.Generic;

namespace Josha.Models.Git
{
    internal sealed record GitCommit(
        string Hash,
        string ShortHash,
        string AuthorName,
        DateTimeOffset Date,
        string Subject,
        string Body,
        IReadOnlyList<string> ParentHashes)
    {
        public string DateDisplay => Date.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

        public bool IsMergeCommit => ParentHashes.Count > 1;
    }
}
