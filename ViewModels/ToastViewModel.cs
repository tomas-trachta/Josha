using System.Windows.Input;

namespace Josha.ViewModels
{
    internal enum ToastSeverity { Info, Success, Warning, Error }

    internal sealed class ToastViewModel : BaseViewModel
    {
        public string Text { get; }
        public ToastSeverity Severity { get; }
        public string? ActionLabel { get; }
        public ICommand? ActionCommand { get; }
        public ICommand DismissCommand { get; }

        public ToastViewModel(
            string text,
            ToastSeverity severity,
            string? actionLabel,
            Action? action,
            Action onDismiss)
        {
            Text = text;
            Severity = severity;
            ActionLabel = actionLabel;
            ActionCommand = action != null
                ? new RelayCommand(_ =>
                {
                    action();
                    onDismiss();
                })
                : null;
            DismissCommand = new RelayCommand(_ => onDismiss());
        }

        public string IconGlyph => Severity switch
        {
            ToastSeverity.Success => "",
            ToastSeverity.Warning => "",
            ToastSeverity.Error   => "",
            _                     => "",
        };

        public string IconBrushKey => Severity switch
        {
            ToastSeverity.Success => "Brush.Toast.Success",
            ToastSeverity.Warning => "Brush.Toast.Warning",
            ToastSeverity.Error   => "Brush.Toast.Error",
            _                     => "Brush.Accent",
        };
    }
}
