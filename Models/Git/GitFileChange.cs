namespace Josha.Models.Git
{
    internal sealed record GitFileChange(string Path, string? OldPath, GitChangeType ChangeType)
    {
        public string DisplayPath => OldPath != null ? $"{OldPath} → {Path}" : Path;

        public string StatusGlyph => ChangeType switch
        {
            GitChangeType.Added => "A",
            GitChangeType.Modified => "M",
            GitChangeType.Deleted => "D",
            GitChangeType.Renamed => "R",
            GitChangeType.Copied => "C",
            GitChangeType.TypeChanged => "T",
            GitChangeType.Unmerged => "U",
            _ => "?",
        };

        public string StatusBrushKey => ChangeType switch
        {
            GitChangeType.Added => "Brush.Toast.Success",
            GitChangeType.Deleted => "Brush.Toast.Error",
            GitChangeType.Modified => "Brush.Toast.Warning",
            GitChangeType.Renamed or GitChangeType.Copied => "Brush.Accent",
            _ => "Brush.OnSurfaceMuted",
        };
    }
}
