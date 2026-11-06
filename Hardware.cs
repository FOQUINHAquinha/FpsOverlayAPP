using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace FpsOverlay
{
    public sealed class HwSnapshot
    {
        public string GpuName = "GPU", CpuName = "CPU";
        public List<string> Gpus = new List<string>();
        public bool Ready, CpuFound;
        public string Error;
        public double? GpuLoad, GpuTemp, GpuPower, GpuClock, VramUsed, VramTotal;
        public double? CpuLoad, CpuTemp, CpuPower, CpuClock;
        public double? RamUsed, RamTotal;
    }

    /// <summary>Lê CPU/GPU com a LibreHardwareMonitorLib (NVIDIA, AMD e Intel) a cada 1 s.</summary>
    public sealed class HardwareService : IDisposable
    {
        Computer _computer;
        volatile HwSnapshot _snap = new HwSnapshot();
        volatile bool _stop;
        string _error;
        public volatile string PreferredGpu = "";

        public HwSnapshot Snapshot => _snap;

        public void Start()
        {
            new Thread(Loop) { IsBackground = true, Name = "Hardware", Priority = ThreadPriority.BelowNormal }.Start();
        }

        void Loop()
        {
            try
            {
                _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
                _computer.Open();
            }
            catch (Exception ex)
            {
                _computer = null;
                _error = ex.Message;
            }

            while (!_stop)
            {
                var s = new HwSnapshot { Error = _error };
                try { Read(s); } catch (Exception ex) { s.Error = ex.Message; }
                ReadRam(s);
                if (!s.CpuFound) s.CpuName = RegistryCpuName();
                s.Ready = true;
                _snap = s;
                Thread.Sleep(1000);
            }
        }

        void Read(HwSnapshot s)
        {
            if (_computer == null) return;
            var gpus = new List<IHardware>();
            IHardware cpu = null;
            foreach (var hw in _computer.Hardware)
            {
                if (hw.HardwareType == HardwareType.Cpu) { if (cpu == null) cpu = hw; else continue; }
                else if (hw.HardwareType == HardwareType.GpuNvidia || hw.HardwareType == HardwareType.GpuAmd || hw.HardwareType == HardwareType.GpuIntel) gpus.Add(hw);
                else continue;
                hw.Update();
                foreach (var sub in hw.SubHardware) sub.Update();
            }

            s.Gpus = gpus.Select(g => g.Name).ToList();
            string pref = PreferredGpu;
            var gpu = gpus.FirstOrDefault(g => g.Name == pref)
                      ?? gpus.OrderByDescending(GpuRank).FirstOrDefault();
            if (gpu != null)
            {
                s.GpuName = CleanGpuName(gpu.Name);
                s.GpuLoad = Get(gpu, SensorType.Load, "GPU Core", "D3D 3D");
                s.GpuTemp = Get(gpu, SensorType.Temperature, "GPU Core", "GPU Hot Spot")
                            ?? Max(gpu, SensorType.Temperature, n => true);
                s.GpuPower = Get(gpu, SensorType.Power, "GPU Package", "GPU Power", "GPU Board Power", "GPU Core")
                             ?? Max(gpu, SensorType.Power, n => true);
                s.GpuClock = Get(gpu, SensorType.Clock, "GPU Core");
                s.VramUsed = Get(gpu, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used");
                s.VramTotal = Get(gpu, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total");
            }

            if (cpu != null)
            {
                s.CpuFound = true;
                s.CpuName = CleanCpuName(cpu.Name);
                s.CpuLoad = Get(cpu, SensorType.Load, "CPU Total");
                s.CpuTemp = Get(cpu, SensorType.Temperature, "Core (Tctl/Tdie)", "CPU Package", "Core Max", "Tctl", "Tdie", "Core Average")
                            ?? Max(cpu, SensorType.Temperature, n => n.StartsWith("Core") || n.StartsWith("CCD"));
                s.CpuPower = Get(cpu, SensorType.Power, "Package", "CPU Package");
                s.CpuClock = Max(cpu, SensorType.Clock, n => n.Contains("Core #"))
                             ?? Max(cpu, SensorType.Clock, n => n.StartsWith("Core"));
            }
        }

        static double GpuRank(IHardware g)
        {
            int vendor = g.HardwareType == HardwareType.GpuNvidia ? 2 : g.HardwareType == HardwareType.GpuAmd ? 1 : 0;
            double mem = Get(g, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total") ?? 0;
            return vendor * 1_000_000 + mem;
        }

        static IEnumerable<ISensor> AllSensors(IHardware h)
        {
            foreach (var x in h.Sensors) yield return x;
            foreach (var sub in h.SubHardware)
                foreach (var x in AllSensors(sub)) yield return x;
        }

        static double? Get(IHardware h, SensorType type, params string[] names)
        {
            var list = AllSensors(h).Where(x => x.SensorType == type && x.Value.HasValue).ToList();
            foreach (var n in names)
            {
                var m = list.FirstOrDefault(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
                if (m != null) return m.Value.Value;
            }
            return null;
        }

        static double? Max(IHardware h, SensorType type, Func<string, bool> pred)
        {
            double? r = null;
            foreach (var x in AllSensors(h))
                if (x.SensorType == type && x.Value.HasValue && x.Value.Value > 0 && pred(x.Name))
                    r = r.HasValue ? Math.Max(r.Value, x.Value.Value) : x.Value.Value;
            return r;
        }

        static void ReadRam(HwSnapshot s)
        {
            var m = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
            if (Native.GlobalMemoryStatusEx(ref m))
            {
                s.RamTotal = m.ullTotalPhys / 1048576.0;
                s.RamUsed = (m.ullTotalPhys - m.ullAvailPhys) / 1048576.0;
            }
        }

        static string RegistryCpuName()
        {
            try
            {
                using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                if (k?.GetValue("ProcessorNameString") is string n) return CleanCpuName(n);
            }
            catch { }
            return "CPU";
        }

        public static string CleanCpuName(string n)
        {
            n = Regex.Replace(n, @"\((R|TM|tm|r)\)", "");
            n = Regex.Replace(n, @"\s+CPU\s+@.*$", "");
            n = Regex.Replace(n, @"\s+\d+-Core Processor.*$", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"\s+with Radeon.*$", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"^\d+(st|nd|rd|th) Gen\s+", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"^(AMD|Intel)\s+", "");
            n = n.Replace("Core ", "").Replace("Processor", "");
            return Regex.Replace(n, @"\s+", " ").Trim().ToUpperInvariant();
        }

        public static string CleanGpuName(string n)
        {
            n = Regex.Replace(n, @"\((R|TM|tm|r)\)", "");
            n = Regex.Replace(n, @"^(NVIDIA\s+)?(GeForce\s+)?", "");
            n = Regex.Replace(n, @"^(AMD\s+)?(Radeon\s+)?", "");
            n = Regex.Replace(n, @"^Intel\s+", "");
            n = n.Replace("Graphics", "");
            n = Regex.Replace(n, @"\s+", " ").Trim();
            return n.Length == 0 ? "GPU" : n.ToUpperInvariant();
        }

        public void Dispose()
        {
            _stop = true;
            try { _computer?.Close(); } catch { }
        }
    }
}
