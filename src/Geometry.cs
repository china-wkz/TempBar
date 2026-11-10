using System;

namespace TempBar
{
    internal static class Geometry
    {
        /// <summary>Locate the primary taskbar (Shell_TrayWnd) and its notification area.</summary>
        public static bool TryGetTaskbar(out RECT bar, out RECT tray, out IntPtr taskbar)
        {
            bar = default(RECT);
            tray = default(RECT);
            taskbar = Native.FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero || !Native.GetWindowRect(taskbar, out bar)) return false;

            IntPtr trayWnd = Native.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            if (trayWnd != IntPtr.Zero) Native.GetWindowRect(trayWnd, out tray);
            return true;
        }
    }
}
