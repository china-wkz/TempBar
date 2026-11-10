using System;
using System.IO;
using System.Text;

namespace TempBar
{
    /// <summary>Tiny append-only logger (truncated on each start). Zero cost when unused.</summary>
    internal static class Log
    {
        private static readonly object Gate = new object();
        private static string _path;
        private static bool _disabled;

        public static string Path
        {
            get
            {
                if (_path == null) _path = System.IO.Path.Combine(Storage.Dir, "tempbar.log");
                return _path;
            }
        }

        public static void Reset()
        {
            if (_disabled) return;
            try
            {
                File.WriteAllText(Path,
                    "TempBar log " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine, Encoding.UTF8);
            }
            catch { _disabled = true; }
        }

        public static void Write(string message)
        {
            if (_disabled) return;
            try
            {
                lock (Gate)
                {
                    File.AppendAllText(Path,
                        DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { _disabled = true; }
        }

        public static void Error(Exception ex)
        {
            if (ex == null) return;
            Write("ERROR " + ex.GetType().Name + ": " + ex.Message + " | " +
                  (ex.StackTrace ?? string.Empty).Replace("\r\n", " / "));
        }
    }
}
