using Josha.Services;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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

    // Wraps IAssocHandler / SHAssocEnumHandlers — the same "recommended apps"
    // list and invocation mechanism Explorer's own "Open with" menu uses, so
    // picking an entry here behaves identically (including reusing a running
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
                var hr = SHAssocEnumHandlers(extension, ASSOC_FILTER.ASSOC_FILTER_RECOMMENDED, out var enumHandlers);
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

        // Passes every path in one IDataObject (CF_HDROP) — the same as
        // selecting multiple files in Explorer and choosing "Open with" —
        // so editors that reuse a running window (VS Code, Sublime, ...)
        // open all of them as tabs in that one instance instead of separate
        // windows, rather than Josha invoking the handler once per file.
        internal static bool Invoke(IAssocHandler handler, IReadOnlyList<string> filePaths)
        {
            var dataObject = new System.Windows.DataObject();
            var files = new StringCollection();
            foreach (var path in filePaths) files.Add(path);
            dataObject.SetFileDropList(files);
            var comDataObject = (System.Runtime.InteropServices.ComTypes.IDataObject)dataObject;

            try
            {
                // Invoke() is documented as unsafe for multi-item selections;
                // CreateInvoker is the API meant for that case. Falls back to
                // Invoke() only if a handler doesn't support CreateInvoker.
                var hr = handler.CreateInvoker(comDataObject, out var invoker);
                if (hr.Succeeded && invoker != null)
                    return invoker.Invoke().Succeeded;

                return handler.Invoke(comDataObject).Succeeded;
            }
            catch (Exception ex)
            {
                Log.Warn(LogCat, "Invoke failed", ex);
                return false;
            }
        }
    }
}
