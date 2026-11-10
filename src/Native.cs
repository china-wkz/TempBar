using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace TempBar
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SIZE { public int CX, CY; public SIZE(int cx, int cy) { CX = cx; CY = cy; } }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    internal static class Native
    {
        // ---------- window styles / messages ----------
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TOPMOST = 0x00000008;

        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int MA_NOACTIVATE = 3;
        public const int MA_ACTIVATE = 1;
        public const int VK_RBUTTON = 0x02;
        public const int WM_NCHITTEST = 0x0084;
        public const int HTTRANSPARENT = -1;
        public const int WM_DISPLAYCHANGE = 0x007E;
        public const int WM_SETTINGCHANGE = 0x001A;

        public const int ULW_ALPHA = 0x00000002;
        public const byte AC_SRC_OVER = 0x00;
        public const byte AC_SRC_ALPHA = 0x01;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        public const int MONITOR_DEFAULTTOPRIMARY = 1;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr child, string cls, string title);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam,
            int flags, int timeoutMs, out IntPtr result);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll")]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        // ---------- layered window rendering ----------
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        // ---------- dpi / theme ----------
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("shcore.dll")]
        public static extern int SetProcessDpiAwareness(int value);

        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
        public static extern int SetPreferredAppMode(int mode); // 1 = allow dark, 2 = force dark

        [DllImport("uxtheme.dll", EntryPoint = "#133")]
        public static extern int FlushMenuThemes();

        // ---------- memory ----------
        [DllImport("psapi.dll")]
        public static extern bool EmptyWorkingSet(IntPtr hProcess);

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_MEMORY_COUNTERS
        {
            public uint cb;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
        }

        [DllImport("psapi.dll", SetLastError = true)]
        public static extern bool GetProcessMemoryInfo(IntPtr hProcess, out PROCESS_MEMORY_COUNTERS counters, uint size);

        /// <summary>Resident working set in bytes, or 0 when it cannot be read.</summary>
        public static long GetWorkingSetBytes()
        {
            PROCESS_MEMORY_COUNTERS counters;
            counters.cb = 0;
            if (!GetProcessMemoryInfo(GetCurrentProcess(), out counters, (uint)Marshal.SizeOf(typeof(PROCESS_MEMORY_COUNTERS))))
                return 0;
            return (long)counters.WorkingSetSize.ToUInt64();
        }

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        // ---------- helpers ----------
        public static void EnableDpiAwareness()
        {
            try
            {
                // -4 = DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
                if (!SetProcessDpiAwarenessContext(new IntPtr(-4)))
                    SetProcessDpiAwareness(2); // PROCESS_PER_MONITOR_DPI_AWARE
            }
            catch { /* older systems: stay system-DPI aware */ }
        }

        public static void EnableDarkMode()
        {
            try
            {
                SetPreferredAppMode(2); // ForceDark
                FlushMenuThemes();
            }
            catch { }
        }

        public static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static bool IsLightTheme()
        {
            try
            {
                object v = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 0);
                return v is int && (int)v != 0;
            }
            catch { return false; }
        }

        public static int PackExStyle(int style)
        {
            return style | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
        }

        /// <summary>Relaunch this executable elevated. Returns false when the user declines UAC.</summary>
        public static bool TryRelaunchElevated(string[] args, bool alreadyAttempted)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (string a in args)
                {
                    if (a.Equals("--elevate-attempt", StringComparison.OrdinalIgnoreCase)) continue;
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append('"').Append(a.Replace("\"", "\\\"")).Append('"');
                }
                if (sb.Length > 0) sb.Append(' ');
                sb.Append("--elevate-attempt");

                var psi = new ProcessStartInfo
                {
                    FileName = System.Windows.Forms.Application.ExecutablePath,
                    Arguments = sb.ToString(),
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void TrimWorkingSet()
        {
            try { EmptyWorkingSet(GetCurrentProcess()); } catch { }
        }
    }
}
