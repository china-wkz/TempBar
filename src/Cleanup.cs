using System;
using System.IO;

namespace TempBar
{
    /// <summary>
    /// Leaves nothing behind: our temporary workspace, the sensor driver's extracted files and any
    /// trace of the earlier installer-based version.
    /// </summary>
    internal static class Cleanup
    {
        public static void Run()
        {
            try
            {
                if (Directory.Exists(Storage.Dir))
                {
                    foreach (string file in Directory.GetFiles(Storage.Dir))
                    {
                        try { File.Delete(file); } catch { }
                    }
                    try { Directory.Delete(Storage.Dir, true); } catch { }
                }
            }
            catch { }

            // LibreHardwareMonitor extracts "<ProcessName>.sys" (WinRing0) and registers "R0<ProcessName>".
            string[] directories =
            {
                Path.GetTempPath(),
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.CurrentDirectory
            };

            string processName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
            string[] patterns =
            {
                processName + ".sys",
                "WinRing0*.sys",
                "LibreHardwareMonitor*.sys",
                "LHM*.sys"
            };

            foreach (string directory in directories)
            {
                foreach (string pattern in patterns)
                {
                    try
                    {
                        foreach (string file in Directory.GetFiles(directory, pattern))
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                    catch { }
                }
            }

            RemoveDriverService("R0" + processName);
        }

        private static void RemoveDriverService(string serviceName)
        {
            foreach (string verb in new[] { "stop", "delete" })
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("sc.exe", verb + " " + serviceName)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(psi))
                    {
                        process.WaitForExit(3000);
                    }
                }
                catch { }
            }
        }

        /// <summary>Best effort removal of a previously installed (non-portable) copy.</summary>
        public static void LegacyInstall()
        {
            try
            {
                string programFiles = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TempBar");
                if (Directory.Exists(programFiles))
                {
                    try { Directory.Delete(programFiles, true); }
                    catch { Log.Write("legacy dir still present: " + programFiles); }
                }
            }
            catch { }

            try
            {
                string appData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TempBar");
                if (Directory.Exists(appData) && !string.Equals(appData, Storage.Dir, StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(appData, true);
            }
            catch { }

            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null && key.GetValue("TempBar") != null) key.DeleteValue("TempBar", false);
                }
            }
            catch { }
        }
    }
}
