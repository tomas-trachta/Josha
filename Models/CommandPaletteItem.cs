namespace Josha.Models
{
    internal sealed class CommandPaletteItem
    {
        public string Category { get; init; } = "";
        public string Title { get; init; } = "";
        public string Subtitle { get; init; } = "";
        public Action? Action { get; init; }
        public int CategoryOrder { get; init; }
    }
}
