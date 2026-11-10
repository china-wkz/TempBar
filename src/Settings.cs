using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace TempBar
{
    internal enum TextTheme { Auto = 0, White = 1, Dark = 2 }

    /// <summary>Plain key=value settings file: no serializer, no extra assemblies.</summary>
    internal sealed class Settings
    {
        public static string FilePath
        {
            get { return System.IO.Path.Combine(Storage.Dir, "config.ini"); }
        }

        public int IntervalMs;
        public bool ShowCpu;
        public bool ShowGpu;
        public int RightGap;
        public bool Locked;
        public bool AutoElevate;
        public TextTheme Theme;

        /// <summary>Portable build: nothing is written to disk, so a run leaves no trace.</summary>
        public const bool Portable = true;

        public Settings()
        {
            IntervalMs = 2000;
            ShowCpu = true;
            ShowGpu = true;
            RightGap = 6;
            Locked = false;
            AutoElevate = true;
            Theme = TextTheme.Auto;
        }

        public static Settings Load()
        {
            var s = new Settings();
            if (Portable) return s;
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    int iv;
                    bool ok = int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out iv);
                    switch (key.ToLowerInvariant())
                    {
                        case "intervalms": if (ok) s.IntervalMs = Clamp(iv, 250, 10000); break;
                        case "showcpu": if (ok) s.ShowCpu = iv != 0; break;
                        case "showgpu": if (ok) s.ShowGpu = iv != 0; break;
                        case "rightgap": if (ok) s.RightGap = Clamp(iv, -2000, 4000); break;
                        case "locked": if (ok) s.Locked = iv != 0; break;
                        case "autoelevate": if (ok) s.AutoElevate = iv != 0; break;
                        case "theme": if (ok) s.Theme = (TextTheme)Clamp(iv, 0, 2); break;
                    }
                }
            }
            catch (Exception ex) { Log.Error(ex); }
            return s;
        }

        public void Save()
        {
            if (Portable) return;
            try
            {
                Directory.CreateDirectory(Storage.Dir);
                var sb = new StringBuilder();
                sb.AppendLine("# TempBar settings");
                sb.AppendLine("IntervalMs=" + IntervalMs);
                sb.AppendLine("ShowCpu=" + (ShowCpu ? 1 : 0));
                sb.AppendLine("ShowGpu=" + (ShowGpu ? 1 : 0));
                sb.AppendLine("RightGap=" + RightGap);
                sb.AppendLine("Locked=" + (Locked ? 1 : 0));
                sb.AppendLine("AutoElevate=" + (AutoElevate ? 1 : 0));
                sb.AppendLine("Theme=" + (int)Theme);
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }
}
