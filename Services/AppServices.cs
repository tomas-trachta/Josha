using Josha.Business;
using Josha.Business.Ftp;
using Josha.Models;

namespace Josha.Services
{
    internal static class AppServices
    {
        public static SnapshotService Snapshot { get; private set; } = null!;
        public static ToastService Toast { get; private set; } = null!;
        public static FileOperationQueue Queue { get; private set; } = null!;

        public static AppSettings Settings { get; private set; } = new();
        public static event Action? SettingsChanged;

        public static void Initialize()
        {
            Log.Initialize();
            SnapshotComponent.MigrateLegacyOnStartup();
            Snapshot = new SnapshotService();
            Toast = new ToastService();
            Queue = new FileOperationQueue();
            Queue.Start();

            Settings = SettingsComponent.Load();

            RemoteConnectionPool.SetSiteUpdateCallback(SiteManagerComponent.Upsert);
        }

        public static void UpdateSettings(AppSettings next)
        {
            SettingsComponent.Save(next);
            Settings = next;
            SettingsChanged?.Invoke();
        }
    }
}
