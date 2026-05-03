using Josha.ViewModels;
using System.Collections.ObjectModel;
using System.Windows;

namespace Josha.Services
{
    internal sealed class ToastService
    {
        private static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(4);

        private const int MaxToasts = 6;

        public ObservableCollection<ToastViewModel> Toasts { get; } = new();

        public void Info(string text, TimeSpan? autoDismiss = null) =>
            Show(text, ToastSeverity.Info, autoDismiss ?? DefaultLifetime, null, null);

        public void Success(string text, TimeSpan? autoDismiss = null) =>
            Show(text, ToastSeverity.Success, autoDismiss ?? DefaultLifetime, null, null);

        public void Warning(string text, TimeSpan? autoDismiss = null) =>
            Show(text, ToastSeverity.Warning, autoDismiss ?? DefaultLifetime, null, null);

        public void Error(string text, string? actionLabel = null, Action? action = null) =>
            Show(text, ToastSeverity.Error, DefaultLifetime, actionLabel, action);

        public void Show(
            string text,
            ToastSeverity severity,
            TimeSpan? autoDismiss,
            string? actionLabel,
            Action? action)
        {
            if (string.IsNullOrEmpty(text)) return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            // Build + add + schedule auto-dismiss all on the dispatcher so the
            // dismiss task captures a non-null reference. Doing it from the
            // calling thread races: the InvokeAsync callback hadn't run yet
            // when we checked `created`, so dismiss was never scheduled.
            dispatcher.InvokeAsync(() =>
            {
                ToastViewModel? created = null;
                created = new ToastViewModel(
                    text, severity, actionLabel, action,
                    onDismiss: () => Remove(created!));
                Toasts.Add(created);
                while (Toasts.Count > MaxToasts) Toasts.RemoveAt(0);

                if (autoDismiss is { } life)
                {
                    var toRemove = created;
                    _ = Task.Delay(life).ContinueWith(_ =>
                    {
                        Application.Current?.Dispatcher.InvokeAsync(() => Remove(toRemove));
                    });
                }
            });
        }

        private void Remove(ToastViewModel toast)
        {
            if (Toasts.Contains(toast)) Toasts.Remove(toast);
        }
    }
}
