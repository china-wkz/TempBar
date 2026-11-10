using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace TempBar
{
    /// <summary>
    /// Background sampler. CPU via LibreHardwareMonitor (CPU subsystem only, ~0.3 ms/poll),
    /// GPU via NVML (~0.05 us/poll). Runs on a low-priority thread so the UI thread stays idle.
    /// </summary>
    internal sealed class SensorService : IDisposable
    {
        private const int NvmlSuccess = 0;
        private const uint NvmlTemperatureGpu = 0;

        private readonly ManualResetEventSlim _wake = new ManualResetEventSlim(false);
        private Thread _thread;
        private volatile bool _stop;
        private volatile int _intervalMs = 1000;

        public volatile int CpuTemp = -1;   // -1 = unavailable
        public volatile int GpuTemp = -1;
        public volatile bool CpuReady;
        public volatile bool GpuReady;

        private volatile bool _reinit;

        /// <summary>Ask the polling thread to drop every handle and rediscover the sensors.</summary>
        public void RequestReinit()
        {
            _reinit = true;
            _wake.Set();
        }

        // LibreHardwareMonitor (CPU only)
        private Computer _computer;
        private IHardware _cpuHardware;
        private ISensor _cpuSensor;
        private DateTime _lastCpuAttempt = DateTime.MinValue;
        private int _cpuFailStreak;

        // NVML
        private bool _nvmlLoaded;
        private volatile IntPtr _nvmlDevice = IntPtr.Zero;
        private DateTime _lastGpuAttempt = DateTime.MinValue;

        public int IntervalMs
        {
            get { return _intervalMs; }
            set
            {
                _intervalMs = value < 250 ? 250 : (value > 10000 ? 10000 : value);
                _wake.Set();
            }
        }

        public void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Loop) { IsBackground = true, Name = "TempBar.Poll", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }

        private void Loop()
        {
            while (!_stop)
            {
                if (_reinit)
                {
                    _reinit = false;
                    CloseCpu();
                    _lastCpuAttempt = DateTime.MinValue;
                    _nvmlDevice = IntPtr.Zero;
                    _lastGpuAttempt = DateTime.MinValue;
                }

                long started = Stopwatch.GetTimestamp();
                try
                {
                    PollCpu();
                    PollGpu();
                }
                catch (Exception ex) { Log.Error(ex); }

                int elapsedMs = (int)((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);

                // Hand the touched pages back to the OS once the resident set grows. NVML alone keeps
                // ~20 MB mapped, so trimming is what keeps the footprint near zero - but doing it on
                // every single cycle would just cause page thrash, hence the threshold.
                if (Native.GetWorkingSetBytes() > 8L * 1024 * 1024) Native.TrimWorkingSet();

                int wait = _intervalMs - elapsedMs;
                if (wait < 100) wait = 100;

                if (!_stop)
                {
                    try { _wake.Wait(wait); } catch { Thread.Sleep(wait); }
                    _wake.Reset();
                }
            }
        }

        // ------------------------------------------------------------------ CPU
        private DateTime _lastElevationCheck = DateTime.MinValue;
        private bool _elevated;
        private bool _warnedNoAdmin;

        private bool IsElevatedCached()
        {
            if ((DateTime.UtcNow - _lastElevationCheck).TotalSeconds > 60)
            {
                _elevated = Native.IsElevated();
                _lastElevationCheck = DateTime.UtcNow;
            }
            return _elevated;
        }

        private void PollCpu()
        {
            if (_cpuSensor == null)
            {
                // Without administrator rights the kernel driver cannot load, so LHM would only
                // burn CPU on failed attempts. Skip it entirely and look again once a minute.
                if (!IsElevatedCached())
                {
                    CpuTemp = -1;
                    CpuReady = false;
                    if (!_warnedNoAdmin)
                    {
                        _warnedNoAdmin = true;
                        Log.Write("CPU sensor skipped: process is not elevated.");
                    }
                    return;
                }

                // retry initialisation at most every 20 s (driver may appear late / after resume)
                if ((DateTime.UtcNow - _lastCpuAttempt).TotalSeconds < 20) return;
                _lastCpuAttempt = DateTime.UtcNow;
                OpenCpu();
                if (_cpuSensor == null) { CpuTemp = -1; return; }
            }

            try
            {
                _cpuHardware.Update();
                float? v = _cpuSensor.Value;
                bool valid = v.HasValue && v.Value > 1f && v.Value < 150f;
                CpuTemp = valid ? (int)Math.Round(v.Value) : -1;
                CpuReady = valid;
                if (!valid)
                {
                    if (++_cpuFailStreak >= 5)
                    {
                        Log.Write("CPU sensor returned no data; reinitialising LHM.");
                        _cpuFailStreak = 0;
                        CloseCpu();
                    }
                }
                else _cpuFailStreak = 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                CloseCpu();
                CpuTemp = -1;
            }
        }

        private void OpenCpu()
        {
            try
            {
                if (_computer == null)
                {
                    _computer = new Computer { IsCpuEnabled = true };
                    _computer.Open();
                }

                _cpuHardware = null;
                _cpuSensor = null;
                foreach (IHardware hw in _computer.Hardware)
                {
                    if (hw.HardwareType == HardwareType.Cpu) { _cpuHardware = hw; break; }
                }
                if (_cpuHardware == null) return;

                _cpuSensor = PickCpuTemperature(_cpuHardware);
                Log.Write("CPU sensor: " + (_cpuSensor == null ? "not found" : _cpuSensor.Name) +
                          " | admin=" + Native.IsElevated());
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                CloseCpu();
            }
        }

        private static ISensor PickCpuTemperature(IHardware hw)
        {
            ISensor best = null;
            int bestScore = 0;
            foreach (ISensor s in hw.Sensors)
            {
                if (s.SensorType != SensorType.Temperature) continue;
                string n = s.Name ?? string.Empty;
                int score = 1;
                if (n.IndexOf("Tctl/Tdie", StringComparison.OrdinalIgnoreCase) >= 0) score = 100;
                else if (n.IndexOf("CPU Package", StringComparison.OrdinalIgnoreCase) >= 0) score = 95;
                else if (n.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0) score = 90;
                else if (n.IndexOf("Tctl", StringComparison.OrdinalIgnoreCase) >= 0) score = 85;
                else if (n.IndexOf("Tdie", StringComparison.OrdinalIgnoreCase) >= 0) score = 80;
                else if (n.IndexOf("Core Max", StringComparison.OrdinalIgnoreCase) >= 0) score = 70;
                else if (n.IndexOf("Core Average", StringComparison.OrdinalIgnoreCase) >= 0) score = 65;
                if (score > bestScore) { bestScore = score; best = s; }
            }
            return best;
        }

        private void CloseCpu()
        {
            try { if (_computer != null) _computer.Close(); } catch { }
            _computer = null;
            _cpuHardware = null;
            _cpuSensor = null;
            CpuReady = false;
        }

        // ------------------------------------------------------------------ GPU
        private void PollGpu()
        {
            if (_nvmlDevice == IntPtr.Zero)
            {
                if ((DateTime.UtcNow - _lastGpuAttempt).TotalSeconds < 15) { GpuTemp = -1; return; }
                _lastGpuAttempt = DateTime.UtcNow;
                OpenGpu();
                if (_nvmlDevice == IntPtr.Zero) { GpuTemp = -1; return; }
            }

            try
            {
                uint temp;
                int rc = NvmlDeviceGetTemperature(_nvmlDevice, NvmlTemperatureGpu, out temp);
                bool valid = rc == NvmlSuccess && temp > 0 && temp < 150;
                GpuTemp = valid ? (int)temp : -1;
                GpuReady = valid;
                if (rc != NvmlSuccess && rc != 3 /* NotSupported */) GpuTemp = -1;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                _nvmlDevice = IntPtr.Zero;
                GpuReady = false;
                GpuTemp = -1;
            }
        }

        private void OpenGpu()
        {
            try
            {
                if (!_nvmlLoaded)
                {
                    int rc = NvmlInit();
                    if (rc != NvmlSuccess)
                    {
                        Log.Write("NVML init failed: " + rc);
                        return;
                    }
                    _nvmlLoaded = true;
                }

                uint count;
                if (NvmlDeviceGetCount(out count) != NvmlSuccess || count == 0)
                {
                    Log.Write("NVML: no devices");
                    return;
                }

                IntPtr dev;
                if (NvmlDeviceGetHandleByIndex(0, out dev) == NvmlSuccess && dev != IntPtr.Zero)
                {
                    _nvmlDevice = dev;
                    Log.Write("GPU sensor: NVML device 0 of " + count);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        // ------------------------------------------------------------------ NVML P/Invoke
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlInit_v2")]
        private static extern int NvmlInit();

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlDeviceGetCount_v2")]
        private static extern int NvmlDeviceGetCount(out uint count);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
        private static extern int NvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlDeviceGetTemperature")]
        private static extern int NvmlDeviceGetTemperature(IntPtr device, uint sensorType, out uint temperature);

        public void Dispose()
        {
            _stop = true;
            try { _wake.Set(); } catch { }
            try { if (_thread != null) _thread.Join(500); } catch { }
            CloseCpu();
        }
    }
}
