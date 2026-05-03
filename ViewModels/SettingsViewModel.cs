using Josha.Models;

namespace Josha.ViewModels
{
    internal class SettingsViewModel : BaseViewModel
    {
        private string _editorPath;
        private string _theme;
        private bool _confirmDeletePermanent;
        private string _defaultViewMode;
        private double _fontScale;

        public string EditorPath
        {
            get => _editorPath;
            set { if (_editorPath == value) return; _editorPath = value; OnPropertyChanged(); }
        }

        public string Theme
        {
            get => _theme;
            set { if (_theme == value) return; _theme = value; OnPropertyChanged(); }
        }

        public bool ConfirmDeletePermanent
        {
            get => _confirmDeletePermanent;
            set { if (_confirmDeletePermanent == value) return; _confirmDeletePermanent = value; OnPropertyChanged(); }
        }

        public string DefaultViewMode
        {
            get => _defaultViewMode;
            set { if (_defaultViewMode == value) return; _defaultViewMode = value; OnPropertyChanged(); }
        }

        public double FontScale
        {
            get => _fontScale;
            set
            {
                var clamped = Math.Round(Math.Clamp(value, 0.85, 1.5), 2);
                if (Math.Abs(_fontScale - clamped) < 0.001) return;
                _fontScale = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FontScaleDisplay));
            }
        }

        public string FontScaleDisplay => $"{_fontScale:0.00}×";

        public string[] ThemeOptions { get; } = new[] { "Dark", "Light", "System" };
        public string[] ViewModeOptions { get; } = new[] { "List", "Tiles", "DiskUsage" };

        public SettingsViewModel(AppSettings src)
        {
            _editorPath = src.EditorPath ?? "";
            _theme = string.IsNullOrEmpty(src.Theme) ? "Dark" : src.Theme;
            _confirmDeletePermanent = src.ConfirmDeletePermanent;
            _defaultViewMode = string.IsNullOrEmpty(src.DefaultViewMode) ? "List" : src.DefaultViewMode;
            _fontScale = src.FontScale <= 0 ? 1.0 : Math.Round(Math.Clamp(src.FontScale, 0.85, 1.5), 2);
        }

        public AppSettings ToModel() => new()
        {
            EditorPath = (EditorPath ?? "").Trim(),
            Theme = Theme,
            ConfirmDeletePermanent = ConfirmDeletePermanent,
            DefaultViewMode = DefaultViewMode,
            FontScale = FontScale,
        };
    }
}
