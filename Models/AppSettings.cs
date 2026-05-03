namespace Josha.Models
{
    internal sealed class AppSettings
    {
        public string EditorPath { get; set; } = "";
        public string Theme { get; set; } = "Dark";
        public bool ConfirmDeletePermanent { get; set; } = true;
        public string DefaultViewMode { get; set; } = "List";
        public double FontScale { get; set; } = 1.0;

        public AppSettings Clone() => (AppSettings)MemberwiseClone();
    }
}
