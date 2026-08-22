using Josha.Business;
using Josha.Models;
using Josha.Services;
using Josha.ViewModels;
using Josha.Views;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Josha
{
    public partial class MainWindow : Window
    {
        private readonly AppShellViewModel _shell = new();
        private const double CollapsedSidebarWidth = 36;
        private double _lastExpandedSidebarWidth = 220;
        private bool _supportsRoundedCorners;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _shell;

            HistorySidebar.ExpandedChanged += OnSidebarExpandedChanged;

            LeftPane.Activated += vm => _shell.SetActive(vm);
            RightPane.Activated += vm => _shell.SetActive(vm);

            // Clicking a tab in the inactive column must promote that column to
            // active — without this, the right column's tab updates its
            // ActiveTab while AppShell.ActiveColumn stays on the left, leaving
            // F-key targeting and the active-border highlight desynced from
            // what the user just clicked.
            LeftTabBar.TabActivated  += vm => { _shell.SetActive(vm); FocusActivePaneListBoth(); };
            RightTabBar.TabActivated += vm => { _shell.SetActive(vm); FocusActivePaneListBoth(); };

            _shell.BookmarksPickerRequested += () =>
            {
                var dlg = new BookmarksDialog(_shell) { Owner = this };
                dlg.ShowDialog();
            };

            _shell.OverwriteResolver = names =>
            {
                var sheet = new OverwriteSheet(names) { Owner = this };
                sheet.ShowDialog();
                return sheet.Result;
            };

            _shell.NewConnectionRequested = () =>
            {
                var sheet = new NewConnectionSheet { Owner = this };
                return sheet.ShowDialog() == true ? sheet.Result : null;
            };

            _shell.SiteManagerRequested = () =>
            {
                var sheet = new SiteManagerSheet { Owner = this };
                return sheet.ShowDialog() == true ? sheet.Result : null;
            };

            _shell.ViewFileRequested = (title, localPath) =>
            {
                var viewer = new InternalViewer { Owner = this };
                viewer.Load(localPath, title);
                viewer.Show();
            };

            _shell.GitHistoryRequested = repoRoot =>
            {
                var gitWindow = new GitHistoryWindow { Owner = this };
                gitWindow.Load(repoRoot);
                gitWindow.Show();
            };

            _shell.PatternPromptRequested = label =>
            {
                var dlg = new PatternPromptDialog(label) { Owner = this };
                return dlg.ShowDialog() == true ? dlg.Result : null;
            };

            _shell.SettingsRequested = () =>
            {
                var sheet = new SettingsSheet { Owner = this };
                return sheet.ShowDialog() == true;
            };

            _shell.CommandPaletteRequested = items =>
            {
                var palette = new CommandPalette(items) { Owner = this };
                palette.ShowDialog();
                return null;
            };

            PreviewKeyDown += OnPreviewKeyDown;

            StateChanged += (_, _) => SyncMaxState();
            SourceInitialized += (_, _) =>
            {
                // WindowStyle=None + WindowChrome answers WM_GETMINMAXINFO with
                // the full screen size, ignoring the taskbar — so the maximized
                // window extends past the work area and the bottom rows are
                // clipped (off-screen or behind the taskbar). Hooking the
                // message lets us return the monitor's rcWork instead.
                var hwnd = new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

                // The Window itself isn't AllowsTransparency, so the corner
                // pixels WindowRoot's own rounding "cuts away" only look right
                // when DWM actually crops the real window surface to match
                // (Windows 11). Without that, those pixels would render as an
                // opaque black square poking out from behind the rounded
                // corners against anything but a black desktop — worse than
                // the plain square window this replaces. So on a DWM miss
                // (Windows 10), stay square rather than risk that.
                _supportsRoundedCorners = ApplyDwmRoundedCorners(hwnd);
                if (!_supportsRoundedCorners) WindowRoot.CornerRadius = new CornerRadius(0);

                SyncMaxState();
            };

            var scale = AppServices.Settings.FontScale;
            if (scale > 0 && System.Math.Abs(scale - 1.0) > 0.01)
                WindowRoot.LayoutTransform = new ScaleTransform(scale, scale);

            RestoreWindowLayout(AppServices.Settings.Window);
            if (AppServices.Settings.Session != null)
                _shell.RestoreSession(AppServices.Settings.Session);

            Closing += OnClosingSaveState;
        }

        // Applies the saved bounds only if they'd still land at least partly on
        // some connected monitor — otherwise a since-unplugged second monitor
        // would leave the window opening off-screen with no way to drag it back.
        private void RestoreWindowLayout(WindowLayoutState? layout)
        {
            if (layout == null || layout.Width <= 0 || layout.Height <= 0) return;

            var onScreen = layout.Left + layout.Width > SystemParameters.VirtualScreenLeft
                && layout.Left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
                && layout.Top + layout.Height > SystemParameters.VirtualScreenTop
                && layout.Top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
            if (!onScreen) return;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = layout.Left;
            Top = layout.Top;
            Width = layout.Width;
            Height = layout.Height;
            if (layout.IsMaximized) WindowState = WindowState.Maximized;
        }

        private void OnClosingSaveState(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            var isMaximized = WindowState == WindowState.Maximized;
            var bounds = isMaximized ? RestoreBounds : new Rect(Left, Top, Width, Height);

            var settings = AppServices.Settings.Clone();
            settings.Window = new WindowLayoutState
            {
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
                IsMaximized = isMaximized,
            };
            settings.Session = _shell.CaptureSession();

            SettingsComponent.Save(settings);
        }

        // The actual maximized-clipping fix is in WmGetMinMaxInfo (clamps to
        // monitor work area). This just toggles the 1px outline + chrome glyph.
        private void SyncMaxState()
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowRoot.BorderThickness = new Thickness(0);
                WindowRoot.CornerRadius = new CornerRadius(0);
                MaxButton.Content = ""; // ChromeRestore
                MaxButton.ToolTip = "Restore";
            }
            else
            {
                WindowRoot.BorderThickness = new Thickness(1);
                if (_supportsRoundedCorners) WindowRoot.CornerRadius = new CornerRadius(8);
                MaxButton.Content = ""; // ChromeMaximize
                MaxButton.ToolTip = "Maximize";
            }
        }

        private const int WM_GETMINMAXINFO = 0x0024;

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return;

            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref mi)) return;

            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            // ptMaxPosition is in monitor-local coordinates: offset of the work
            // area from the monitor's top-left (non-zero when the taskbar is on
            // the top/left edge).
            mmi.ptMaxPosition.x = mi.rcWork.left - mi.rcMonitor.left;
            mmi.ptMaxPosition.y = mi.rcWork.top - mi.rcMonitor.top;
            mmi.ptMaxSize.x = mi.rcWork.right - mi.rcWork.left;
            mmi.ptMaxSize.y = mi.rcWork.bottom - mi.rcWork.top;
            mmi.ptMaxTrackSize.x = mmi.ptMaxSize.x;
            mmi.ptMaxTrackSize.y = mmi.ptMaxSize.y;
            Marshal.StructureToPtr(mmi, lParam, true);
        }

        // Real Windows 11 per-window corner rounding (DWM crops the composited
        // surface itself), rather than a WPF-drawn approximation — so every
        // pixel of the window, including anything a nested control draws flush
        // to the edge, comes out correctly rounded for free. No-ops silently on
        // Windows 10 (the attribute is unrecognized there), leaving today's
        // square corners as the fallback.
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        private static bool ApplyDwmRoundedCorners(IntPtr hwnd)
        {
            var preference = DWMWCP_ROUND;
            var hr = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            return hr == 0; // S_OK
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private void OnSidebarExpandedChanged(bool isExpanded)
        {
            if (isExpanded)
            {
                SidebarColumn.MinWidth = 160;
                SidebarColumn.Width = new GridLength(_lastExpandedSidebarWidth);
            }
            else
            {
                if (SidebarColumn.ActualWidth > 0)
                    _lastExpandedSidebarWidth = SidebarColumn.ActualWidth;
                SidebarColumn.MinWidth = CollapsedSidebarWidth;
                SidebarColumn.Width = new GridLength(CollapsedSidebarWidth);
            }
        }

        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximized();
                e.Handled = true;
            }
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e)
            => SystemCommands.MinimizeWindow(this);

        private void OnMaxRestoreClick(object sender, RoutedEventArgs e)
            => ToggleMaximized();

        private void OnCloseClick(object sender, RoutedEventArgs e)
            => SystemCommands.CloseWindow(this);

        private void ToggleMaximized()
        {
            if (WindowState == WindowState.Maximized)
                SystemCommands.RestoreWindow(this);
            else
                SystemCommands.MaximizeWindow(this);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
            {
                var list = _shell.ActivePane?.List;
                if (list != null)
                {
                    list.ShowHiddenFiles = !list.ShowHiddenFiles;
                    e.Handled = true;
                }
                return;
            }

            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                var pane = _shell.ActiveColumn == _shell.LeftColumn ? LeftPane : RightPane;
                pane.FocusFilterBox();
                e.Handled = true;
                return;
            }

            // Handled here rather than as a window KeyBinding: TextBox has a
            // class-level Ctrl+Z (EditingCommands.Undo) that swallows the key
            // before a bubble-routed KeyBinding ever sees it whenever focus is
            // in the path bar, filter box, or an inline rename editor.
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_shell.UndoCommand.CanExecute(null))
                {
                    _shell.UndoCommand.Execute(null);
                    e.Handled = true;
                }
                return;
            }

            // Ctrl+V pastes files from the shell clipboard into the active pane.
            // Skipped when a TextBox has focus so plain text paste still works.
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (Keyboard.FocusedElement is TextBox) return;
                if (_shell.PasteCommand.CanExecute(null))
                {
                    _shell.PasteCommand.Execute(null);
                    e.Handled = true;
                }
                return;
            }

            // Ctrl+C / Ctrl+X put the active pane's selection on the shell
            // clipboard (CF_HDROP + "Preferred DropEffect"), so Ctrl+V pastes
            // into the other pane — or into Explorer. TextBox keeps native text
            // copy/cut. Ctrl+Shift+C and Ctrl+Alt+C are handled by window
            // KeyBindings (CopyPath / CopyName) and don't reach here.
            if ((e.Key == Key.C || e.Key == Key.X) && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (Keyboard.FocusedElement is TextBox) return;
                var cmd = e.Key == Key.X ? _shell.ClipboardCutCommand : _shell.ClipboardCopyCommand;
                if (cmd.CanExecute(null))
                {
                    cmd.Execute(null);
                    e.Handled = true;
                }
                return;
            }

            // Ctrl+Tab cycles tabs in the active column. Handled here (not as a
            // window KeyBinding) because WPF's KeyboardNavigation consumes Tab
            // before InputBindings get a look.
            if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                e.Handled = true;
                var col = _shell.ActiveColumn;
                if (col != null && col.Tabs.Count > 1)
                {
                    var cmd = (Keyboard.Modifiers & ModifierKeys.Shift) != 0
                        ? col.PrevTabCommand
                        : col.NextTabCommand;
                    if (cmd.CanExecute(null)) cmd.Execute(null);
                    FocusActivePaneListBoth();
                }
                return;
            }

            // Ctrl+Left / Ctrl+Right switch panes. Skipped only when a text input
            // has focus so word-jump caret movement still works in path / filter
            // bars. Always Handled otherwise so WPF's input chain can't shuffle
            // focus to a neighbouring control.
            if ((e.Key == Key.Left || e.Key == Key.Right)
                && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (Keyboard.FocusedElement is TextBox) return;

                e.Handled = true;
                var target = e.Key == Key.Left ? _shell.LeftColumn : _shell.RightColumn;
                if (_shell.ActiveColumn != target)
                    _shell.SetActiveColumn(target);
                FocusActivePaneListBoth();
            }
        }

        // Belt-and-braces focus: immediately AND deferred. Some focus moves are
        // synchronous from binding updates triggered by SetActiveColumn; others
        // (like ListView container realisation) only complete on the next tick.
        private void FocusActivePaneListBoth()
        {
            var pane = _shell.ActiveColumn == _shell.LeftColumn ? LeftPane : RightPane;
            pane.FocusList();
            Dispatcher.BeginInvoke(new Action(pane.FocusList),
                System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
