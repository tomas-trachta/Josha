using Josha.Models.Git;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Josha.Business.Git
{
    // Turns a single-file unified diff (as produced by `git diff-tree -p`)
    // into side-by-side rows. Removed/added runs between two context lines
    // are paired up positionally so a changed block reads as old-vs-new
    // rather than a flat top-to-bottom list of - then + lines.
    internal static class DiffParser
    {
        internal static List<GitDiffRow> ParseUnifiedDiff(string diffText)
        {
            var rows = new List<GitDiffRow>();
            if (string.IsNullOrEmpty(diffText)) return rows;

            var pendingOld = new List<string>();
            var pendingNew = new List<string>();
            int oldLine = 0, newLine = 0;
            bool inHunk = false;

            void FlushPending()
            {
                int count = System.Math.Max(pendingOld.Count, pendingNew.Count);
                for (int i = 0; i < count; i++)
                {
                    var hasOld = i < pendingOld.Count;
                    var hasNew = i < pendingNew.Count;
                    rows.Add(new GitDiffRow
                    {
                        OldLineNumber = hasOld ? oldLine++ : null,
                        OldText = hasOld ? pendingOld[i] : "",
                        OldKind = hasOld ? GitDiffLineKind.Removed : GitDiffLineKind.Empty,
                        NewLineNumber = hasNew ? newLine++ : null,
                        NewText = hasNew ? pendingNew[i] : "",
                        NewKind = hasNew ? GitDiffLineKind.Added : GitDiffLineKind.Empty,
                    });
                }
                pendingOld.Clear();
                pendingNew.Clear();
            }

            foreach (var raw in diffText.Replace("\r\n", "\n").Split('\n'))
            {
                if (raw.StartsWith("@@"))
                {
                    FlushPending();
                    inHunk = true;
                    (oldLine, newLine) = ParseHunkHeader(raw);
                    rows.Add(new GitDiffRow { IsHunkHeader = true, HeaderText = raw });
                    continue;
                }

                if (!inHunk) continue; // diff --git / index / --- / +++ preamble

                // Every real diff content line starts with ' ' / '+' / '-' / '\' —
                // a zero-length line only occurs from Split('\n') producing a
                // trailing empty element for the newline git always ends its
                // output with. Treating it as content created a phantom blank
                // red/green row at the end of every diff.
                if (raw.Length == 0) continue;

                switch (raw[0])
                {
                    case '-':
                        pendingOld.Add(raw[1..]);
                        break;
                    case '+':
                        pendingNew.Add(raw[1..]);
                        break;
                    case ' ':
                        FlushPending();
                        rows.Add(new GitDiffRow
                        {
                            OldLineNumber = oldLine++,
                            OldText = raw[1..],
                            OldKind = GitDiffLineKind.Context,
                            NewLineNumber = newLine++,
                            NewText = raw[1..],
                            NewKind = GitDiffLineKind.Context,
                        });
                        break;
                    // '\' marks "No newline at end of file" — nothing to render.
                }
            }
            FlushPending();
            return rows;
        }

        private static readonly Regex HunkHeaderPattern = new(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@", RegexOptions.Compiled);

        private static (int oldStart, int newStart) ParseHunkHeader(string header)
        {
            var match = HunkHeaderPattern.Match(header);
            if (!match.Success) return (1, 1);
            return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
        }
    }
}
