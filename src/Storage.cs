using System;
using System.IO;

namespace TempBar
{
    /// <summary>
    /// Portable build: everything lives in %TEMP%\TempBar and is removed when the tool exits, so no
    /// settings or traces survive a run. Falls back to %TEMP% itself if that folder is not writable.
    /// </summary>
    internal static class Storage
    {
        private static string _dir;

        public static string Dir
        {
            get
            {
                if (_dir != null) return _dir;

                string[] candidates =
                {
                    Path.Combine(Path.GetTempPath(), "TempBar"),
                    Path.Combine(Path.GetTempPath(), "TempBar-" + System.Diagnostics.Process.GetCurrentProcess().Id)
                };

                foreach (string candidate in candidates)
                {
                    try
                    {
                        Directory.CreateDirectory(candidate);
                        string probe = Path.Combine(candidate, ".writetest");
                        File.WriteAllText(probe, "1");
                        File.Delete(probe);
                        _dir = candidate;
                        return _dir;
                    }
                    catch { }
                }

                _dir = Path.GetTempPath();
                return _dir;
            }
        }
    }
}
