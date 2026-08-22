using Josha.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static Vanara.PInvoke.Shell32;

namespace Josha.Business
{
    internal sealed class OpenWithHandlerEntry
    {
        public string DisplayName { get; }
        public ImageSource? Icon { get; }
        internal IAssocHandler Handler { get; }

        internal OpenWithHandlerEntry(string displayName, ImageSource? icon, IAssocHandler handler)
        {
            DisplayName = displayName;
            Icon = icon;
            Handler = handler;
        }
    }

    // Wraps IAssocHandler / SHAssocEnumHandlers — the same handler list and
    // invocation mechanism Explorer's own "Open with" menu uses (ASSOC_FILTER_NONE
    // returns every registered handler, not just the curated "recommended" subset),
    // so picking an entry here behaves identically (including reusing a running
    // instance for editors that support it).
    internal static class OpenWithComponent
    {
        private const string LogCat = "OpenWith";

        internal static List<OpenWithHandlerEntry> GetHandlers(string extension)
        {
            var results = new List<OpenWithHandlerEntry>();
            if (string.IsNullOrEmpty(extension)) return results;

            try
            {
                var hr = SHAssocEnumHandlers(extension, ASSOC_FILTER.ASSOC_FILTER_NONE, out var enumHandlers);
                if (hr.Failed || enumHandlers == null) return results;

                var buffer = new IAssocHandler[1];
                while (true)
                {
                    var nextHr = enumHandlers.Next(1, buffer, out var fetched);
                    if (nextHr.Failed || fetched == 0) break;

                    var handler = buffer[0];
                    if (handler.GetUIName(out var name).Succeeded && !string.IsNullOrEmpty(name))
                    {
                        var icon = handler.GetIconLocation(out var iconPath, out var iconIndex).Succeeded
                            ? ExtractIcon(iconPath, iconIndex)
                            : null;
                        results.Add(new OpenWithHandlerEntry(name, icon, handler));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCat, $"Enumerating handlers for {extension} failed", ex);
            }
            return results.OrderBy(h => h.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        // GetIconLocation gives back the same "path,index" pair Explorer
        // resolves icons from (e.g. "C:\...\Code.exe,0" or a negative index
        // for a resource ID) — ExtractIconEx accepts that pair directly.
        private static ImageSource? ExtractIcon(string iconPath, int iconIndex)
        {
            if (string.IsNullOrEmpty(iconPath)) return null;

            try
            {
                var fetched = ExtractIconEx(iconPath, iconIndex, 1, out var largeIcons, out var smallIcons);
                using var large = largeIcons?.Length > 0 ? largeIcons[0] : null;
                using var small = smallIcons?.Length > 0 ? smallIcons[0] : null;
                if (fetched == 0 || small == null || small.IsInvalid) return null;

                var bitmap = Imaging.CreateBitmapSourceFromHIcon(small.DangerousGetHandle(), Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCat, $"Extracting icon from {iconPath} failed", ex);
                return null;
            }
        }

        // IAssocHandler.Invoke()/CreateInvoker() go through COM activation
        // that a lot of recommended handlers don't actually support for a
        // bare CF_HDROP selection — it surfaces as Windows' generic "This
        // file does not have an app associated with it" error even though
        // the handler is a perfectly normal desktop app. GetName() sidesteps
        // all of that: it's the same resolved executable path Explorer would
        // launch, so we start it ourselves with every file as an argument —
        // one process, so editors that reuse a running window (VS Code,
        // Sublime, ...) open them as tabs in that one instance.
        internal static bool Launch(IAssocHandler handler, IReadOnlyList<string> filePaths)
        {
            if (!handler.GetName(out var exePath).Succeeded || string.IsNullOrEmpty(exePath))
                return false;

            try
            {
                var psi = new ProcessStartInfo(exePath) { UseShellExecute = false };
                foreach (var path in filePaths) psi.ArgumentList.Add(path);
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCat, $"Launching {exePath} failed", ex);
                return false;
            }
        }
    }
}
