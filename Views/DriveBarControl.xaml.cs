using Josha.Services;
using Josha.ViewModels;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Josha.Views
{
    public partial class DriveBarControl : UserControl
    {
        public sealed class Drive
        {
            public string Letter { get; init; } = "";
            public string Tooltip { get; init; } = "";
            public bool IsActive { get; init; }
        }

        public DriveBarControl()
        {
            InitializeComponent();
            Loaded += (_, _) => RefreshDrives();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is FilePaneViewModel oldVm)
                oldVm.PropertyChanged -= OnVmPropertyChanged;

            if (e.NewValue is FilePaneViewModel newVm)
            {
                newVm.PropertyChanged += OnVmPropertyChanged;
                RefreshDrives();
            }
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FilePaneViewModel.DriveLetter))
                RefreshDrives();
        }

        // DriveInfo enumeration + per-drive .IsReady / .VolumeLabel can block for
        // tens of seconds on flaky removable / network drives — must not run on
        // the UI thread.
        private void RefreshDrives() => _ = RefreshDrivesAsync();

        private async Task RefreshDrivesAsync()
        {
            var current = (DataContext as FilePaneViewModel)?.DriveLetter ?? "";

            List<Drive> drives;
            try
            {
                drives = await Task.Run(() => EnumerateDrives(current)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log.Warn("DriveBar", "Drive enumeration failed", ex);
                drives = new List<Drive>();
            }

            DriveItems.ItemsSource = drives;
        }

        private static List<Drive> EnumerateDrives(string current)
        {
            var drives = new List<Drive>();
            try
            {
                foreach (var di in DriveInfo.GetDrives())
                {
                    bool ready;
                    try { ready = di.IsReady; } catch { continue; }
                    if (!ready) continue;

                    string letter;
                    try
                    {
                        letter = di.RootDirectory.FullName
                            .TrimEnd('\\', '/').TrimEnd(':')
                            .ToUpperInvariant();
                    }
                    catch { continue; }
                    if (letter.Length == 0) continue;

                    string label;
                    try { label = string.IsNullOrEmpty(di.VolumeLabel) ? letter + ":" : $"{letter}: {di.VolumeLabel}"; }
                    catch { label = letter + ":"; }

                    drives.Add(new Drive
                    {
                        Letter = letter,
                        Tooltip = label,
                        IsActive = string.Equals(letter, current, StringComparison.OrdinalIgnoreCase),
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warn("DriveBar", "Drive enumeration failed", ex);
            }
            return drives;
        }

        private void OnDriveClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not string letter) return;
            if (DataContext is not FilePaneViewModel vm) return;

            var rootPath = letter + @":\";
            _ = vm.NavigateAsync(rootPath);
            e.Handled = true;
        }
    }
}
