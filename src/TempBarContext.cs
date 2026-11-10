using System;
using System.Drawing;
using System.Windows.Forms;

namespace TempBar
{
    internal sealed class TempBarContext : ApplicationContext
    {
        private enum Cmd
        {
            ToggleCpu, ToggleGpu,
            Interval1, Interval2, Interval3, Interval5,
            ToggleLock, ThemeAuto, ThemeWhite, ThemeDark,
            Reinit, RestartElevated, About, Exit
        }

        private readonly Settings _cfg;
        private readonly SensorService _sensors;
        private readonly WidgetForm _widget;
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu;

        private readonly Timer _renderTimer;     // 1 s: only touches a few ints when nothing changed
        private readonly Timer _topTimer;        // 500 ms: single SetWindowPos, keeps us above the taskbar
        private readonly Timer _geometryTimer;   // 5 s: re-reads the taskbar rect

        private Icon _trayIcon;
        private bool _lightTheme;
        private bool _quitting;
        private bool _elevated;
        private DateTime _elevationCheckedUtc = DateTime.MinValue;
        private bool _iconActive = true;

        private int _lastCpu = -2, _lastGpu = -2;
        private bool _lastShowCpu, _lastShowGpu;
        private string _lastTrayText = string.Empty;

        public TempBarContext(bool cpuOnly, bool gpuOnly)
        {
            Log.Reset();
            _elevated = Native.IsElevated();
            Log.Write("start | admin=" + _elevated + " | x64=" + Environment.Is64BitProcess +
                      " | dir=" + Storage.Dir);

            _cfg = Settings.Load();
            if (cpuOnly) _cfg.ShowGpu = false;
            if (gpuOnly) _cfg.ShowCpu = false;
            _lightTheme = ResolveLightTheme();

            _sensors = new SensorService { IntervalMs = _cfg.IntervalMs };
            _sensors.CpuEnabled = _cfg.ShowCpu;
            _sensors.GpuEnabled = _cfg.ShowGpu;
            _sensors.Start();

            _widget = new WidgetForm { Locked = _cfg.Locked, RightGap = _cfg.RightGap };
            _widget.ApplyColors(_lightTheme);
            _widget.ContextMenuRequested += (s, e) => ShowMenu(Cursor.Position);
            _widget.QuitRequested += (s, e) => Quit();
            _widget.DragFinished += (s, e) =>
            {
                _cfg.RightGap = _widget.RightGap;
                _cfg.Save();
            };

            _menu = BuildMenu();

            _trayIcon = IconFactory.CreateThermometer(TrayIconSize(), _lightTheme, true);
            _tray = new NotifyIcon
            {
                Icon = _trayIcon,
                Text = "TempBar",
                Visible = true,
                ContextMenuStrip = _menu
            };
            _tray.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) ShowMenu(Cursor.Position); };
            _tray.DoubleClick += (s, e) => ShowMenu(Cursor.Position);

            _renderTimer = new Timer { Interval = Math.Max(500, _cfg.IntervalMs) };
            _renderTimer.Tick += (s, e) => RenderTick();
            _renderTimer.Start();

            _topTimer = new Timer { Interval = 500 };
            _topTimer.Tick += (s, e) => _widget.EnsureTopMost();
            _topTimer.Start();

            _geometryTimer = new Timer { Interval = 10000 };
            _geometryTimer.Tick += (s, e) => ApplyGeometry();
            _geometryTimer.Start();

            RenderTick();
            ApplyGeometry();
        }

        // ------------------------------------------------------------------ ticks

        private void RenderTick()
        {
            int cpu = _sensors.CpuTemp;
            int gpu = _sensors.GpuTemp;

            if (cpu != _lastCpu || gpu != _lastGpu ||
                _cfg.ShowCpu != _lastShowCpu || _cfg.ShowGpu != _lastShowGpu)
            {
                _lastCpu = cpu;
                _lastGpu = gpu;
                _lastShowCpu = _cfg.ShowCpu;
                _lastShowGpu = _cfg.ShowGpu;

                _widget.RenderContent(cpu, gpu, _cfg.ShowCpu, _cfg.ShowGpu);
                _widget.ShowWidget();
                ApplyGeometry();
                UpdateTrayInfo(cpu, gpu);
            }
        }

        private void ApplyGeometry()
        {
            RECT bar, tray;
            IntPtr taskbar;
            if (!Geometry.TryGetTaskbar(out bar, out tray, out taskbar)) return;
            _widget.ApplyTaskbarGeometry(taskbar, bar, tray, 0);
        }

        private void UpdateTrayInfo(int cpu, int gpu)
        {
            string cpuText = cpu > 0 ? cpu + "\u00B0C" : "--";
            string gpuText = gpu > 0 ? gpu + "\u00B0C" : "--";

            var parts = new System.Collections.Generic.List<string>(2);
            if (_cfg.ShowCpu)
            {
                if (!IsElevated()) parts.Add("CPU " + cpuText + "(需管理员)");
                else if (_sensors.CpuReady) parts.Add("CPU " + cpuText);
                else parts.Add("CPU 驱动不可用");
            }
            if (_cfg.ShowGpu) parts.Add("GPU " + gpuText);

            string text = parts.Count == 0 ? "TempBar" : string.Join("   ", parts);
            if (text.Length > 63) text = text.Substring(0, 63);
            if (text != _lastTrayText)
            {
                _lastTrayText = text;
                try { _tray.Text = text; } catch { }
            }

            bool active = (_cfg.ShowCpu && _sensors.CpuReady) || (_cfg.ShowGpu && _sensors.GpuReady);
            if (active != _iconActive)
            {
                _iconActive = active;
                RefreshTrayIcon();
            }
        }

        /// <summary>Elevation is cached for a minute - WindowsIdentity.GetCurrent() is not cheap.</summary>
        private bool IsElevated()
        {
            if ((DateTime.UtcNow - _elevationCheckedUtc).TotalSeconds > 60)
            {
                _elevated = Native.IsElevated();
                _elevationCheckedUtc = DateTime.UtcNow;
            }
            return _elevated;
        }

        private void RefreshTrayIcon()
        {
            Icon old = _trayIcon;
            _trayIcon = IconFactory.CreateThermometer(TrayIconSize(), _lightTheme, _iconActive);
            _tray.Icon = _trayIcon;
            if (old != null) old.Dispose();
        }

        private static int TrayIconSize()
        {
            int s = SystemInformation.SmallIconSize.Width;
            if (s < 16) s = 16;
            if (s > 48) s = 48;
            return s;
        }

        private bool ResolveLightTheme()
        {
            switch (_cfg.Theme)
            {
                case TextTheme.White: return false;
                case TextTheme.Dark: return true;
                default: return Native.IsLightTheme();
            }
        }

        // ------------------------------------------------------------------ menu

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip
            {
                Renderer = new DarkRenderer(),
                Font = new Font("Microsoft YaHei UI", 9f),
                BackColor = Color.FromArgb(43, 43, 43),
                ForeColor = Color.FromArgb(235, 235, 235)
            };
            menu.Opening += (s, e) => SyncMenuChecks();

            menu.Items.Add(Item("显示 CPU 温度", Cmd.ToggleCpu));
            menu.Items.Add(Item("显示 GPU 温度", Cmd.ToggleGpu));
            menu.Items.Add(new ToolStripSeparator());

            var interval = new ToolStripMenuItem("刷新间隔");
            ((ToolStripDropDownMenu)interval.DropDown).ShowCheckMargin = true;
            interval.DropDown.Renderer = menu.Renderer;
            interval.DropDown.BackColor = menu.BackColor;
            interval.DropDown.Font = menu.Font;
            interval.DropDownItems.Add(Item("1 秒", Cmd.Interval1));
            interval.DropDownItems.Add(Item("2 秒", Cmd.Interval2));
            interval.DropDownItems.Add(Item("3 秒", Cmd.Interval3));
            interval.DropDownItems.Add(Item("5 秒", Cmd.Interval5));
            menu.Items.Add(interval);

            menu.Items.Add(Item("锁定位置", Cmd.ToggleLock));

            var theme = new ToolStripMenuItem("文字颜色");
            ((ToolStripDropDownMenu)theme.DropDown).ShowCheckMargin = true;
            theme.DropDown.Renderer = menu.Renderer;
            theme.DropDown.BackColor = menu.BackColor;
            theme.DropDown.Font = menu.Font;
            theme.DropDownItems.Add(Item("自动（跟随系统）", Cmd.ThemeAuto));
            theme.DropDownItems.Add(Item("白色", Cmd.ThemeWhite));
            theme.DropDownItems.Add(Item("深色", Cmd.ThemeDark));
            menu.Items.Add(theme);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("重新读取传感器", Cmd.Reinit));
            menu.Items.Add(Item("以管理员身份重启", Cmd.RestartElevated));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("关于", Cmd.About));
            menu.Items.Add(Item("退出", Cmd.Exit));
            return menu;
        }

        private ToolStripMenuItem Item(string text, Cmd cmd)
        {
            var item = new ToolStripMenuItem(text) { Tag = cmd };
            item.Click += (s, e) => Execute((Cmd)((ToolStripMenuItem)s).Tag);
            return item;
        }

        private void SyncMenuChecks()
        {
            SyncMenuItems(_menu.Items);
        }

        private void SyncMenuItems(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                var mi = item as ToolStripMenuItem;
                if (mi == null) continue;

                if (mi.Tag is Cmd) ApplyCheck(mi, (Cmd)mi.Tag);

                // recurse so 刷新间隔 / 文字颜色 also show the active choice
                if (mi.HasDropDownItems) SyncMenuItems(mi.DropDownItems);
            }
        }

        private void ApplyCheck(ToolStripMenuItem mi, Cmd cmd)
        {
            {
                switch (cmd)
                {
                    case Cmd.ToggleCpu: mi.Checked = _cfg.ShowCpu; break;
                    case Cmd.ToggleGpu: mi.Checked = _cfg.ShowGpu; break;
                    case Cmd.ToggleLock: mi.Checked = _cfg.Locked; break;
                    case Cmd.Interval1: mi.Checked = _cfg.IntervalMs == 1000; break;
                    case Cmd.Interval2: mi.Checked = _cfg.IntervalMs == 2000; break;
                    case Cmd.Interval3: mi.Checked = _cfg.IntervalMs == 3000; break;
                    case Cmd.Interval5: mi.Checked = _cfg.IntervalMs == 5000; break;
                    case Cmd.ThemeAuto: mi.Checked = _cfg.Theme == TextTheme.Auto; break;
                    case Cmd.ThemeWhite: mi.Checked = _cfg.Theme == TextTheme.White; break;
                    case Cmd.ThemeDark: mi.Checked = _cfg.Theme == TextTheme.Dark; break;
                }
            }
        }

        private void Execute(Cmd cmd)
        {
            switch (cmd)
            {
                case Cmd.ToggleCpu:
                    if (!_cfg.ShowCpu && !_cfg.ShowGpu) _cfg.ShowGpu = true;
                    _cfg.ShowCpu = !_cfg.ShowCpu;
                    break;
                case Cmd.ToggleGpu:
                    if (!_cfg.ShowGpu && !_cfg.ShowCpu) _cfg.ShowCpu = true;
                    _cfg.ShowGpu = !_cfg.ShowGpu;
                    break;
                case Cmd.Interval1: SetInterval(1000); break;
                case Cmd.Interval2: SetInterval(2000); break;
                case Cmd.Interval3: SetInterval(3000); break;
                case Cmd.Interval5: SetInterval(5000); break;
                case Cmd.ToggleLock:
                    _cfg.Locked = !_cfg.Locked;
                    _widget.Locked = _cfg.Locked;
                    break;
                case Cmd.ThemeAuto: _cfg.Theme = TextTheme.Auto; ApplyTheme(); break;
                case Cmd.ThemeWhite: _cfg.Theme = TextTheme.White; ApplyTheme(); break;
                case Cmd.ThemeDark: _cfg.Theme = TextTheme.Dark; ApplyTheme(); break;
                case Cmd.Reinit:
                    _sensors.RequestReinit();
                    break;
                case Cmd.RestartElevated:
                    if (IsElevated())
                        MessageBox.Show("当前已经是管理员权限，无需重启。", "TempBar",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    else if (!Native.TryRelaunchElevated(new string[0], false))
                        MessageBox.Show("提权被取消，CPU 温度需要管理员权限才能读取。", "TempBar",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    else
                        Quit();
                    return;
                case Cmd.About:
                    ShowAbout();
                    return;
                case Cmd.Exit:
                    Quit();
                    return;
            }

            _sensors.CpuEnabled = _cfg.ShowCpu;
            _sensors.GpuEnabled = _cfg.ShowGpu;

            _cfg.Save();
            _lastCpu = -2; // force a repaint on the next tick
            RenderTick();
        }

        private void SetInterval(int ms)
        {
            _cfg.IntervalMs = ms;
            _sensors.IntervalMs = ms;
            // keep the UI timer aligned with the sampling cadence: fewer wake-ups, deeper idle
            _renderTimer.Interval = Math.Max(500, ms);
        }

        private void ApplyTheme()
        {
            _lightTheme = ResolveLightTheme();
            _widget.ApplyColors(_lightTheme);
            RefreshTrayIcon();
            _widget.RenderContent(_sensors.CpuTemp, _sensors.GpuTemp, _cfg.ShowCpu, _cfg.ShowGpu);
        }

        private void ShowAbout()
        {
            string cpu = _sensors.CpuTemp > 0 ? _sensors.CpuTemp + " °C" : "不可用";
            string gpu = _sensors.GpuTemp > 0 ? _sensors.GpuTemp + " °C" : "不可用";
            string admin = IsElevated() ? "是" : "否（CPU 温度需要管理员权限）";
            MessageBox.Show(
                "TempBar " + Program.Version + "（绿色版）\n\n" +
                "CPU 温度：" + cpu + "\n" +
                "GPU 温度：" + gpu + "\n" +
                "刷新间隔：" + _cfg.IntervalMs + " ms\n" +
                "管理员权限：" + admin + "\n\n" +
                "退出时会自动清除临时文件与残留。",
                "关于 TempBar", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ------------------------------------------------------------------ menu display

        private void ShowMenu(Point screenPoint)
        {
            _widget.BringForward();
            _menu.Show(screenPoint);
        }

        // ------------------------------------------------------------------ shutdown

        /// <summary>
        /// Quitting is deferred to the next message-loop turn: tearing the menu down from inside its
        /// own click handler throws, and that used to leave the process alive.
        /// </summary>
        private void Quit()
        {
            if (_quitting) return;
            _quitting = true;

            try { _renderTimer.Stop(); _topTimer.Stop(); _geometryTimer.Stop(); } catch { }

            // Defer: run the teardown on the next message-loop turn, never inside the click handler.
            try { _widget.BeginInvoke(new MethodInvoker(Shutdown)); }
            catch { Shutdown(); }
        }

        private void Shutdown()
        {
            try
            {
                _tray.Visible = false;
                _tray.Dispose();
                if (_trayIcon != null) { _trayIcon.Dispose(); _trayIcon = null; }

                _sensors.Dispose();   // closes the sensor driver handles
                _menu.Dispose();

                _widget.Hide();
                _widget.Dispose();
            }
            catch (Exception ex) { Log.Error(ex); }
            finally
            {
                try { Cleanup.Run(); } catch { }
                try { ExitThread(); } catch { }
                // belt and braces: nothing must survive this call
                Environment.Exit(0);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_quitting) Quit();
            base.Dispose(disposing);
        }
    }
}
