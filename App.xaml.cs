using Josha.Business.Ftp;
using Josha.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Josha
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppServices.Initialize();
            ThemeService.Apply(AppServices.Settings.Theme);

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            Exit += OnExit;
        }

        // Sync wait. WPF doesn't await Exit handlers, so async void here returns
        // at the first await and the process tears down before the cleanup
        // tasks finish — pending edit-on-server uploads would be lost.
        // 5s ceiling so a wedged remote can't stall app exit forever.
        private void OnExit(object sender, ExitEventArgs e)
        {
            var shutdown = Task.Run(async () =>
            {
                try { await EditOnServerWatcher.DisposeAllAsync(); } catch { }
                try { await RemoteConnectionPool.ShutdownAsync(); } catch { }
            });

            try { shutdown.Wait(TimeSpan.FromSeconds(5)); }
            catch { }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error("UnhandledException", "Dispatcher exception", e.Exception);
            AppServices.Toast.Error(
                $"Unhandled error: {e.Exception.Message}",
                actionLabel: "Open log",
                action: OpenLogFolder);
            e.Handled = true;
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            var ex = e.Exception.InnerException ?? e.Exception;
            Log.Error("UnhandledException", "Unobserved task exception", ex);
            e.SetObserved();
            Dispatcher.Invoke(() => AppServices.Toast.Error(
                $"Background error: {ex.Message}",
                actionLabel: "Open log",
                action: OpenLogFolder));
        }

        private static void OpenLogFolder()
        {
            var dir = Log.LogDirectory;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Log.Warn("App", "Open log folder failed", ex);
            }
        }
    }
}
