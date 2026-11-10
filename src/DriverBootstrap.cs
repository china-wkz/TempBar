using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace TempBar
{
    /// <summary>
    /// LibreHardwareMonitor extracts its kernel driver next to the executable
    /// (Path.ChangeExtension(processModule.FileName, ".sys")) - if the exe sits on the Desktop, a
    /// TempBar.sys appears there. To avoid that we install the very same driver from our temp
    /// workspace before LHM starts: LHM then simply opens the existing device and never extracts
    /// anything next to the exe.
    /// </summary>
    internal static class DriverBootstrap
    {
        private const string DevicePath = @"\\.\WinRing0_1_2_0";
        private const string DriverResource = "LibreHardwareMonitor.Resources.WinRing0x64.gz";

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint OPEN_EXISTING = 3;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security,
            uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool MoveFileExW(string existing, string target, uint flags);

        private static readonly IntPtr InvalidHandle = new IntPtr(-1);
        private static string _serviceName;
        private static string _driverPath;

        /// <summary>True when our own copy of the driver is running from the temp workspace.</summary>
        public static bool Ready { get; private set; }

        public static bool TryStart()
        {
            if (Ready) return true;
            if (!Native.IsElevated()) return false;

            try
            {
                string modulePath = Process.GetCurrentProcess().MainModule.FileName;
                string baseName = Path.GetFileNameWithoutExtension(modulePath);
                _serviceName = ServiceNameOf(baseName);
                _driverPath = Path.Combine(Storage.Dir, baseName + ".sys");

                // Already serving the device (e.g. from a previous run)? Nothing to do.
                if (CanOpenDevice()) return Ready = true;

                if (!ExtractDriver(_driverPath))
                {
                    Log.Write("driver bootstrap: extraction failed");
                    return false;
                }

                // A stale registration would make CreateService fail; remove it first.
                RunSc("stop " + _serviceName);
                RunSc("delete " + _serviceName);

                int rc = RunSc("create " + _serviceName + " type= kernel start= demand error= normal binPath= \""
                               + _driverPath + "\"");
                if (rc != 0)
                {
                    Log.Write("driver bootstrap: sc create failed (" + rc + ")");
                    Discard();
                    return false;
                }

                rc = RunSc("start " + _serviceName);
                if (rc != 0)
                {
                    Log.Write("driver bootstrap: sc start failed (" + rc + ")");
                    Discard();
                    return false;
                }

                if (!CanOpenDevice())
                {
                    Log.Write("driver bootstrap: device did not appear");
                    Discard();
                    return false;
                }

                Log.Write("driver bootstrap: serving from " + _driverPath);
                return Ready = true;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                Discard();
                return false;
            }
        }

        /// <summary>Same naming rule LibreHardwareMonitor uses: "R0" + process name.</summary>
        private static string ServiceNameOf(string baseName)
        {
            return ("R0" + baseName).Replace(" ", string.Empty).Replace(".", "_");
        }

        private static bool ExtractDriver(string target)
        {
            try
            {
                Assembly assembly = typeof(LibreHardwareMonitor.Hardware.Computer).Assembly;
                using (Stream compressed = assembly.GetManifestResourceStream(DriverResource))
                {
                    if (compressed == null) return false;

                    Directory.CreateDirectory(Path.GetDirectoryName(target));

                    // LibreHardwareMonitor skips the first byte of the embedded resource before gunzip.
                    compressed.Position = 1;
                    using (var gzip = new GZipStream(compressed, CompressionMode.Decompress))
                    using (var output = new FileStream(target, FileMode.Create, FileAccess.Write))
                    {
                        gzip.CopyTo(output);
                    }
                }
                var info = new FileInfo(target);
                if (info.Length < 4096) return false;

                // A real PE image starts with "MZ"; anything else means the resource changed.
                using (var probe = new FileStream(target, FileMode.Open, FileAccess.Read))
                {
                    if (probe.ReadByte() != 'M' || probe.ReadByte() != 'Z') return false;
                }

                Log.Write("driver bootstrap: extracted " + info.Length + " bytes");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                return false;
            }
        }

        private static bool CanOpenDevice()
        {
            IntPtr handle = CreateFileW(DevicePath, GENERIC_READ | GENERIC_WRITE, 0, IntPtr.Zero,
                OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == InvalidHandle || handle == IntPtr.Zero) return false;

            // LHM opens the device with FileShare.None, so never keep the handle around.
            CloseHandle(handle);
            return true;
        }

        private static void Discard()
        {
            RunSc("stop " + _serviceName);
            RunSc("delete " + _serviceName);
            TryDelete(_driverPath);
            Ready = false;
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (File.Exists(path))
                {
                    // The service may still hold the image; schedule removal at next reboot as a fallback.
                    try { File.Delete(path); }
                    catch { MoveFileExW(path, null, 4 /* MOVEFILE_DELAY_UNTIL_REBOOT */); }
                }
            }
            catch { }
        }

        private static int RunSc(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo("sc.exe", arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.Default,
                    StandardErrorEncoding = Encoding.Default
                };
                using (Process process = Process.Start(psi))
                {
                    process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    process.WaitForExit(8000);
                    return process.HasExited ? process.ExitCode : -1;
                }
            }
            catch
            {
                return -1;
            }
        }
    }
}
