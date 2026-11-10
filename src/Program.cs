using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace TempBar
{
    internal static class Program
    {
        public const string Version = "1.1.0";

        /// <summary>
        /// The sensor libraries are embedded in this single executable and loaded straight from
        /// memory, so the tool is one portable file that leaves nothing behind.
        /// </summary>
        private static void HookEmbeddedAssemblies()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string simpleName;
                try { simpleName = new AssemblyName(args.Name).Name; }
                catch { return null; }

                string resource = "TempBar." + simpleName + ".dll";
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                {
                    if (stream == null) return null;
                    var buffer = new byte[stream.Length];
                    int read = 0;
                    while (read < buffer.Length)
                    {
                        int n = stream.Read(buffer, read, buffer.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    return Assembly.Load(buffer);
                }
            };
        }

        [STAThread]
        private static void Main(string[] args)
        {
            HookEmbeddedAssemblies();

            // The sensor driver is extracted into the *current* directory; point that at our temp
            // workspace so nothing ever appears next to the executable (e.g. on the Desktop).
            try { System.IO.Directory.SetCurrentDirectory(Storage.Dir); } catch { }

            bool noElevate = HasFlag(args, "--noelevate");
            bool alreadyAttempted = HasFlag(args, "--elevate-attempt");
            bool elevated = Native.IsElevated();

            // CPU temperatures need the kernel driver, i.e. administrator rights.
            if (!elevated && !noElevate && !alreadyAttempted)
            {
                if (Native.TryRelaunchElevated(args, alreadyAttempted)) return;
            }

            string mutexName = HasFlag(args, "--test") ? @"Local\TempBar.SingleInstance.Test" : @"Local\TempBar.SingleInstance";
            using (var mutex = new Mutex(false, mutexName))
            {
                bool owned;
                try { owned = mutex.WaitOne(0, false); }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned) return;

                Native.EnableDpiAwareness();
                Native.EnableDarkMode();

                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => Log.Error(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error(e.ExceptionObject as Exception);

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                if (elevated) Cleanup.LegacyInstall();

                try
                {
                    using (var context = new TempBarContext())
                    {
                        Application.Run(context);
                    }
                }
                finally
                {
                    Cleanup.Run();
                }

                try { mutex.ReleaseMutex(); } catch { }
            }
        }

        private static bool HasFlag(string[] args, string flag)
        {
            if (args == null) return false;
            foreach (string a in args)
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
