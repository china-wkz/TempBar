using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TempBar
{
    /// <summary>
    /// The taskbar overlay: a borderless layered window that renders only white text on a fully
    /// transparent background. Frames are composed into a 32bpp premultiplied bitmap and pushed
    /// with UpdateLayeredWindow - and only when the displayed text actually changed.
    /// </summary>
    internal sealed class WidgetForm : Form
    {
        private const int PaddingX = 4;
        private const int GapBetweenGroups = 13;
        private const int WM_CLOSE_MESSAGE = 0x0010;

        private Font _valueFont;
        private Color _labelColor;
        private Color _valueColor;

        private Bitmap _bitmap;
        private Graphics _graphics;
        private Graphics _measureGraphics;
        private Bitmap _measureBitmap;

        private string _lastSignature = "\u0000";
        private int _width = 120;
        private int _height = 30;

        private bool _dragging;
        private Point _dragOrigin;

        public bool Locked { get; set; }
        public int RightGap { get; set; }

        public event EventHandler DragFinished;
        public event MouseEventHandler ContextMenuRequested;
        public event EventHandler QuitRequested;

        public WidgetForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            MinimumSize = new Size(1, 1);
            SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint, true);

            _valueFont = new Font("Segoe UI", 9.75f, FontStyle.Bold, GraphicsUnit.Point);

            _measureBitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
            _measureGraphics = Graphics.FromImage(_measureBitmap);
            _measureGraphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            ApplyColors(false);
        }

        public void ApplyColors(bool lightTheme)
        {
            _valueColor = lightTheme ? Color.FromArgb(255, 20, 20, 20) : Color.FromArgb(255, 255, 255, 255);
            _labelColor = lightTheme ? Color.FromArgb(140, 20, 20, 20) : Color.FromArgb(140, 255, 255, 255);
            _lastSignature = "\u0000";
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // NOTE: no WS_EX_NOACTIVATE - the popup menu needs a foreground window, otherwise
                // it can never be dismissed by clicking somewhere else.
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE)
            {
                // left click keeps working as "drag", right click may activate so the menu behaves
                bool rightDown = (Native.GetAsyncKeyState(Native.VK_RBUTTON) & 0x8000) != 0;
                m.Result = (IntPtr)(rightDown ? Native.MA_ACTIVATE : Native.MA_NOACTIVATE);
                return;
            }

            if (m.Msg == WM_CLOSE_MESSAGE)
            {
                EventHandler handler = QuitRequested;
                if (handler != null) { handler(this, EventArgs.Empty); return; }
            }

            base.WndProc(ref m);
        }

        // ---------------------------------------------------------------- public API

        public void RenderContent(int cpuTemp, int gpuTemp, bool showCpu, bool showGpu)
        {
            string cpuText = cpuTemp > 0 ? cpuTemp.ToString() + "\u00B0C" : "--\u00B0C";
            string gpuText = gpuTemp > 0 ? gpuTemp.ToString() + "\u00B0C" : "--\u00B0C";

            string signature = (showCpu ? "1" : "0") + (showGpu ? "1" : "0") + "|" + cpuText + "|" + gpuText +
                               "|" + _valueColor.ToArgb();

            if (signature == _lastSignature) return;
            _lastSignature = signature;

            Compose(showCpu, showGpu, cpuText, gpuText);
            PushToScreen();
        }

        public void ShowWidget()
        {
            if (!Visible) Show();
        }

        public void EnsureTopMost()
        {
            if (!IsHandleCreated) return;
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        public void BringForward()
        {
            if (!IsHandleCreated) return;
            Native.SetForegroundWindow(Handle);
        }

        // ---------------------------------------------------------------- painting

        private void Compose(bool showCpu, bool showGpu, string cpuText, string gpuText)
        {
            // No labels: the left value is the CPU, the right value is the GPU.
            var parts = new System.Collections.Generic.List<Part>(3);
            if (showCpu) parts.Add(new Part(cpuText, _valueFont, _valueColor));
            if (showGpu)
            {
                if (parts.Count > 0) parts.Add(new Part(null, null, Color.Empty));
                parts.Add(new Part(gpuText, _valueFont, _valueColor));
            }
            if (parts.Count == 0) parts.Add(new Part("--", _valueFont, _labelColor));

            float lineHeight = _valueFont.GetHeight(_measureGraphics);
            float width = PaddingX * 2f;
            foreach (Part p in parts)
            {
                if (p.Text == null) { width += GapBetweenGroups; continue; }
                width += Measure(p.Text, p.Font);
            }

            int newWidth = (int)Math.Ceiling(width);
            int newHeight = (int)Math.Ceiling(lineHeight) + 12;
            if (newWidth < 8) newWidth = 8;

            if (_bitmap == null || _bitmap.Width != newWidth || _bitmap.Height != newHeight)
                RecreateBitmaps(newWidth, newHeight);

            _width = newWidth;
            _height = newHeight;

            Graphics g = _graphics;
            g.Clear(Color.FromArgb(1, 0, 0, 0)); // alpha 1: invisible but still hit-testable
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.SmoothingMode = SmoothingMode.None;

            float y = (_height - lineHeight) / 2f;
            float x = PaddingX;
            using (var sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
                foreach (Part p in parts)
                {
                    if (p.Text == null) { x += GapBetweenGroups; continue; }
                    using (var brush = new SolidBrush(p.Color))
                        g.DrawString(p.Text, p.Font, brush, x, y, sf);
                    x += Measure(p.Text, p.Font);
                }
            }

            PremultiplyWhite(_bitmap);
        }

        private float Measure(string text, Font font)
        {
            using (var sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
                return _measureGraphics.MeasureString(text, font, new PointF(0, 0), sf).Width;
            }
        }

        private void RecreateBitmaps(int width, int height)
        {
            if (_graphics != null) { _graphics.Dispose(); _graphics = null; }
            if (_bitmap != null) { _bitmap.Dispose(); _bitmap = null; }
            _bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            _graphics = Graphics.FromImage(_bitmap);
        }

        /// <summary>
        /// Content is pure white, so premultiplied RGB equals the alpha channel - one pass keeps the
        /// glyph edges smooth without any per-pixel colour bookkeeping.
        /// </summary>
        private static void PremultiplyWhite(Bitmap bitmap)
        {
            Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int bytes = Math.Abs(data.Stride) * bitmap.Height;
                byte[] buffer = new byte[bytes];
                Marshal.Copy(data.Scan0, buffer, 0, bytes);
                for (int i = 0; i < bytes; i += 4)
                {
                    byte a = buffer[i + 3];
                    buffer[i] = a;
                    buffer[i + 1] = a;
                    buffer[i + 2] = a;
                }
                Marshal.Copy(buffer, 0, data.Scan0, bytes);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private void PushToScreen()
        {
            if (!IsHandleCreated) CreateHandle();

            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = _bitmap.GetHbitmap(Color.FromArgb(0));
            IntPtr oldBitmap = Native.SelectObject(memDc, hBitmap);
            try
            {
                var size = new SIZE(_bitmap.Width, _bitmap.Height);
                var src = new POINT(0, 0);
                var dst = new POINT(Left, Top);
                var blend = new BLENDFUNCTION
                {
                    BlendOp = Native.AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = Native.AC_SRC_ALPHA
                };
                Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                Native.SelectObject(memDc, oldBitmap);
                Native.DeleteObject(hBitmap);
                Native.DeleteDC(memDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        // ---------------------------------------------------------------- geometry

        public void ApplyTaskbarGeometry(IntPtr taskbar, RECT bar, RECT tray, int gap)
        {
            bool horizontal = (bar.Right - bar.Left) >= (bar.Bottom - bar.Top);
            if (!horizontal)
            {
                int x = bar.Left + ((bar.Right - bar.Left) - _width) / 2;
                int y = bar.Bottom - _height - gap;
                SetBoundsClamped(x, y);
                return;
            }

            int anchorRight = tray.Right > tray.Left && tray.Left >= bar.Left ? tray.Left : bar.Right - 200;
            int newX = anchorRight - RightGap - _width;
            if (newX < bar.Left + 2) newX = bar.Left + 2;
            if (newX + _width > bar.Right - 2) newX = bar.Right - 2 - _width;

            int barHeight = bar.Bottom - bar.Top;
            int newY = bar.Top + (barHeight - _height) / 2;
            SetBoundsClamped(newX, newY);
        }

        private void SetBoundsClamped(int x, int y)
        {
            if (Left == x && Top == y && Width == _width && Height == _height) return;
            SetBounds(x, y, _width, _height);
            if (_bitmap != null) PushToScreen();
        }

        // ---------------------------------------------------------------- interaction

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && !Locked)
            {
                _dragging = true;
                _dragOrigin = Cursor.Position;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;

            Point now = Cursor.Position;
            int dx = now.X - _dragOrigin.X;
            if (dx == 0) return;
            _dragOrigin = now;

            int newX = Left + dx;
            RECT bar, tray;
            IntPtr taskbar;
            Geometry.TryGetTaskbar(out bar, out tray, out taskbar);
            if (newX < bar.Left + 2) newX = bar.Left + 2;
            if (newX + _width > bar.Right - 2) newX = bar.Right - 2 - _width;

            int anchorRight = tray.Right > tray.Left && tray.Left >= bar.Left ? tray.Left : bar.Right - 200;
            RightGap = anchorRight - (newX + _width);
            SetBounds(newX, Top, _width, _height);
            PushToScreen();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button == MouseButtons.Right)
            {
                MouseEventHandler menuHandler = ContextMenuRequested;
                if (menuHandler != null) menuHandler(this, e);
                return;
            }

            if (_dragging)
            {
                _dragging = false;
                EventHandler handler = DragFinished;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left)
            {
                MouseEventHandler handler = ContextMenuRequested;
                if (handler != null) handler(this, e);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_graphics != null) _graphics.Dispose();
                if (_bitmap != null) _bitmap.Dispose();
                if (_measureGraphics != null) _measureGraphics.Dispose();
                if (_measureBitmap != null) _measureBitmap.Dispose();
                if (_valueFont != null) _valueFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private struct Part
        {
            public readonly string Text;
            public readonly Font Font;
            public readonly Color Color;

            public Part(string text, Font font, Color color)
            {
                Text = text;
                Font = font;
                Color = color;
            }
        }
    }
}
