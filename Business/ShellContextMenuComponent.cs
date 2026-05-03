using Josha.Services;
using System.Runtime.InteropServices;
using Vanara.PInvoke;
using static Vanara.PInvoke.Shell32;
using static Vanara.PInvoke.User32;

namespace Josha.Business
{
    // IContextMenu only — IContextMenu2/3 messages aren't forwarded, so
    // owner-drawn extension items render as plain text. All paths must share
    // a parent folder (WPF selection always does, one pane = one folder).
    internal static class ShellContextMenuComponent
    {
        private const string LogCat = "ShellMenu";

        public static void Show(IReadOnlyList<string> paths, IntPtr ownerHwnd, int screenX, int screenY)
        {
            if (paths == null || paths.Count == 0) return;

            // Absolute PIDLs need disposing; the relative ones from
            // SHBindToParent are owned by the parent folder — do not free.
            var absPidls = new List<PIDL>(paths.Count);
            object? parentObj = null;
            object? cmObj = null;

            try
            {
                foreach (var p in paths)
                {
                    if (string.IsNullOrEmpty(p)) continue;
                    var hr = SHParseDisplayName(p, null, out var pidl, 0, out _);
                    if (hr.Succeeded && !pidl.IsNull) absPidls.Add(pidl);
                }
                if (absPidls.Count == 0) return;

                var iidShellFolder = typeof(IShellFolder).GUID;
                var bindHr = SHBindToParent(absPidls[0], iidShellFolder, out parentObj, out var firstRelPidl);
                if (bindHr.Failed) return;
                if (parentObj is not IShellFolder parent) return;

                var relPidls = new IntPtr[absPidls.Count];
                relPidls[0] = firstRelPidl;
                for (int i = 1; i < absPidls.Count; i++)
                {
                    SHBindToParent(absPidls[i], iidShellFolder, out _, out relPidls[i]);
                }

                var iidCm = typeof(IContextMenu).GUID;
                parent.GetUIObjectOf(ownerHwnd, (uint)relPidls.Length, relPidls,
                    iidCm, IntPtr.Zero, out cmObj);
                if (cmObj is not IContextMenu ctxMenu) return;

                using var hmenu = CreatePopupMenu();
                if (hmenu.IsInvalid) return;

                ctxMenu.QueryContextMenu(hmenu, 0, 1, 0x7FFF,
                    CMF.CMF_NORMAL | CMF.CMF_CANRENAME | CMF.CMF_EXPLORE);

                var cmd = TrackPopupMenuEx(
                    hmenu,
                    TrackPopupMenuFlags.TPM_RETURNCMD | TrackPopupMenuFlags.TPM_RIGHTBUTTON,
                    screenX, screenY, ownerHwnd, null);

                if ((int)cmd <= 0) return;

                // lpVerb = MAKEINTRESOURCE(cmd-1); idCmdFirst was 1.
                var ici = new CMINVOKECOMMANDINFOEX
                {
                    cbSize = (uint)Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                    fMask = CMIC.CMIC_MASK_UNICODE,
                    hwnd = ownerHwnd,
                    lpVerb = (IntPtr)((int)cmd - 1),
                    nShow = ShowWindowCommand.SW_NORMAL,
                };
                ctxMenu.InvokeCommand(ici);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCat, $"Shell context menu failed for {paths.Count} item(s)", ex);
            }
            finally
            {
                if (cmObj != null && Marshal.IsComObject(cmObj)) Marshal.ReleaseComObject(cmObj);
                if (parentObj != null && Marshal.IsComObject(parentObj)) Marshal.ReleaseComObject(parentObj);
                foreach (var pidl in absPidls) pidl.Dispose();
            }
        }
    }
}
