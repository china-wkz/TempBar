using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TempBar
{
    /// <summary>Draws the tray glyph at runtime (no image resources shipped, no icon files to load).</summary>
    internal static class IconFactory
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static Icon CreateThermometer(int size, bool lightTheme, bool active)
        {
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    Color ink = lightTheme ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 245, 245, 245);
                    if (!active) ink = Color.FromArgb(255, 140, 140, 140);

                    float cx = size / 2f;
                    float bulbR = size * 0.20f;
                    float bulbCy = size * 0.74f;
                    float tubeW = size * 0.20f;
                    float tubeTop = size * 0.13f;

                    using (var tube = new Pen(ink, tubeW))
                    {
                        tube.StartCap = LineCap.Round;
                        tube.EndCap = LineCap.Round;
                        g.DrawLine(tube, cx, tubeTop, cx, bulbCy - bulbR * 0.35f);
                    }
                    using (var bulb = new SolidBrush(ink))
                        g.FillEllipse(bulb, cx - bulbR, bulbCy - bulbR, bulbR * 2, bulbR * 2);

                    // graduation ticks on the right side of the tube
                    using (var tick = new Pen(ink, Math.Max(1f, size * 0.055f)))
                    {
                        tick.StartCap = LineCap.Round;
                        tick.EndCap = LineCap.Round;
                        for (int i = 0; i < 3; i++)
                        {
                            float y = tubeTop + size * (0.10f + i * 0.13f);
                            g.DrawLine(tick, cx + tubeW * 0.85f, y, cx + tubeW * 1.85f, y);
                        }
                    }
                }

                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (Icon tmp = Icon.FromHandle(hIcon))
                        return (Icon)tmp.Clone();
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }
    }
}
