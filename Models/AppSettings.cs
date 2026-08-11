namespace Josha.Models
{
    internal sealed class AppSettings
    {
        public string EditorPath { get; set; } = "";
        public string Theme { get; set; } = "Dark";
        public bool ConfirmDeletePermanent { get; set; } = true;
        public string DefaultViewMode { get; set; } = "List";
        public double FontScale { get; set; } = 1.0;
        public WindowLayoutState? Window { get; set; }
        public SessionState? Session { get; set; }

        public AppSettings Clone() => (AppSettings)MemberwiseClone();
    }
}
