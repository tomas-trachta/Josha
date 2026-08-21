namespace Josha.Models.Git
{
    // A single visual row of a side-by-side diff. Either a full-width hunk
    // header, or an old/new line pair (one side is blank-filled when the
    // other side has no counterpart, e.g. a pure addition or removal).
    internal sealed record GitDiffRow
    {
        public bool IsHunkHeader { get; init; }
        public string HeaderText { get; init; } = "";

        public int? OldLineNumber { get; init; }
        public string OldText { get; init; } = "";
        public GitDiffLineKind OldKind { get; init; }

        public int? NewLineNumber { get; init; }
        public string NewText { get; init; } = "";
        public GitDiffLineKind NewKind { get; init; }

        public string OldBackgroundKey => OldKind switch
        {
            GitDiffLineKind.Removed => "Brush.Diff.RemovedBg",
            GitDiffLineKind.Empty => "Brush.SurfaceMuted",
            _ => "Brush.Surface",
        };

        public string NewBackgroundKey => NewKind switch
        {
            GitDiffLineKind.Added => "Brush.Diff.AddedBg",
            GitDiffLineKind.Empty => "Brush.SurfaceMuted",
            _ => "Brush.Surface",
        };
    }
}
