using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FpsOverlay
{
    /// <summary>Frametimes de um processo (buffer circular).</summary>
    public sealed class ProcFrames
    {
        public const int Capacity = 200_000;
        public readonly int Pid;
        public string App;
        public string Runtime = "";
        public long LastSeen;
        float[] _buf = new float[1024];
        int _head, _count;

        public ProcFrames(int pid, string app) { Pid = pid; App = app; }

        public int Count => _count;

        public void Add(float ms)
        {
            if (_count == _buf.Length && _buf.Length < Capacity)
            {
                // cresce enquanto ainda não atingiu a capacidade (dados contíguos: head == 0)
                var nb = new float[Math.Min(Capacity, _buf.Length * 2)];
                Array.Copy(_buf, nb, _count);
                _buf = nb;
                _head = _count;
            }
            _buf[_head] = ms;
            _head = (_head + 1) % _buf.Length;
            if (_count < _buf.Length) _count++;
            LastSeen = Environment.TickCount64;
        }

        public float[] Last(int n)
        {
            n = Math.Min(n, _count);
            var r = new float[n];
            int len = _buf.Length;
            int start = (_head - n + len) % len;
            for (int i = 0; i < n; i++) r[i] = _buf[(start + i) % len];
            return r;
        }

        public void Clear() { _head = 0; _count = 0; }
    }

    public sealed class FrameStore
    {
        readonly object _lock = new object();
        readonly Dictionary<int, ProcFrames> _procs = new Dictionary<int, ProcFrames>();

        public void Add(int pid, string app, string runtime, float ms)
        {
            lock (_lock)
            {
                if (!_procs.TryGetValue(pid, out var p))
                    _procs[pid] = p = new ProcFrames(pid, app);
                p.Runtime = runtime;
                p.Add(ms);
            }
        }

        public List<(int pid, string app, long last)> List()
        {
            lock (_lock)
            {
                var l = new List<(int, string, long)>(_procs.Count);
                foreach (var p in _procs.Values) l.Add((p.Pid, p.App, p.LastSeen));
                return l;
            }
        }

        public (string app, string runtime, long last)? Info(int pid)
        {
            lock (_lock)
                return _procs.TryGetValue(pid, out var p) ? (p.App, p.Runtime, p.LastSeen) : null;
        }

        public float[] Last(int pid, int n)
        {
            lock (_lock)
                return _procs.TryGetValue(pid, out var p) ? p.Last(n) : Array.Empty<float>();
        }

        public float[] All(int pid)
        {
            lock (_lock)
                return _procs.TryGetValue(pid, out var p) ? p.Last(p.Count) : Array.Empty<float>();
        }

        public void Reset(int pid)
        {
            lock (_lock)
                if (_procs.TryGetValue(pid, out var p)) p.Clear();
        }

        public void Cleanup(int? keep)
        {
            long now = Environment.TickCount64;
            lock (_lock)
            {
                var dead = new List<int>();
                foreach (var p in _procs.Values)
                    if (now - p.LastSeen > 30_000 && p.Pid != keep) dead.Add(p.Pid);
                foreach (var d in dead) _procs.Remove(d);
            }
        }
    }

    /// <summary>Roda o PresentMon (Intel) e lê os frametimes de todos os processos via stdout.</summary>
    public sealed class PresentMonService : IDisposable
    {
        readonly FrameStore _store;
        Process _proc;
        volatile bool _stopping;
        public volatile string Status = "Iniciando PresentMon...";

        public PresentMonService(FrameStore store) { _store = store; }

        public void Start()
        {
            string exe = Locate();
            if (exe == null)
            {
                Status = "PresentMon não encontrado";
                return;
            }
            new Thread(() => Loop(exe)) { IsBackground = true, Name = "PresentMon" }.Start();
        }

        static string Locate()
        {
            string local = Path.Combine(AppContext.BaseDirectory, "PresentMon.exe");
            if (File.Exists(local)) return local;
            try
            {
                using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("PresentMon.exe");
                if (s == null) return null;
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FpsOverlay");
                Directory.CreateDirectory(dir);
                string target = Path.Combine(dir, "PresentMon.exe");
                if (!File.Exists(target) || new FileInfo(target).Length != s.Length)
                {
                    try
                    {
                        using var f = File.Create(target);
                        s.CopyTo(f);
                    }
                    catch when (File.Exists(target)) { /* em uso: usa o que já existe */ }
                }
                return target;
            }
            catch { return null; }
        }

        void Loop(string exe)
        {
            while (!_stopping)
            {
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };
                foreach (var a in new[] { "--output_stdout", "--no_console_stats",
                                          "--session_name", "FpsOverlaySession", "--stop_existing_session" })
                    psi.ArgumentList.Add(a);

                var err = new StringBuilder();
                try
                {
                    _proc = Process.Start(psi);
                    Native.KillWithThisProcess(_proc);
                    _proc.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
                    _proc.BeginErrorReadLine();
                    ReadOutput(_proc.StandardOutput);
                }
                catch (Exception ex)
                {
                    Status = "Falha ao iniciar PresentMon: " + ex.Message;
                    return;
                }
                if (_stopping) break;

                string e2;
                lock (err) e2 = err.ToString().ToLowerInvariant();
                if (e2.Contains("access") || e2.Contains("admin") || e2.Contains("privilege"))
                {
                    Status = "PresentMon sem permissão de administrador";
                    return;
                }
                Status = "PresentMon parou, reiniciando...";
                Thread.Sleep(2000);
            }
        }

        void ReadOutput(StreamReader r)
        {
            int iApp = -1, iPid = -1, iRt = -1, iMs = -1;
            string line;
            while ((line = r.ReadLine()) != null)
            {
                if (_stopping) return;
                if (line.StartsWith("Application,", StringComparison.Ordinal))
                {
                    var cols = line.Split(',');
                    for (int i = 0; i < cols.Length; i++)
                    {
                        switch (cols[i].Trim())
                        {
                            case "Application": iApp = i; break;
                            case "ProcessID": iPid = i; break;
                            case "PresentRuntime": iRt = i; break;
                            case "MsBetweenPresents": iMs = i; break;
                            case "FrameTime": if (iMs < 0) iMs = i; break;
                        }
                    }
                    Status = (iPid < 0 || iMs < 0) ? "Formato do PresentMon não reconhecido" : "";
                    continue;
                }
                if (iMs < 0 || iPid < 0) continue;
                var parts = line.Split(',');
                if (parts.Length <= Math.Max(iMs, iPid)) continue;
                if (!float.TryParse(parts[iMs], NumberStyles.Float, CultureInfo.InvariantCulture, out float ms) || ms <= 0) continue;
                if (!int.TryParse(parts[iPid], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid)) continue;
                string app = iApp >= 0 && iApp < parts.Length ? parts[iApp] : pid.ToString(CultureInfo.InvariantCulture);
                string rt = iRt >= 0 && iRt < parts.Length ? parts[iRt] : "";
                _store.Add(pid, app, rt, ms);
            }
        }

        public void Dispose()
        {
            _stopping = true;
            try { if (_proc != null && !_proc.HasExited) _proc.Kill(); } catch { }
        }
    }

    public static class Stats
    {
        public static (double? avg, double? low1, double? low01) Compute(float[] ms, int windowSeconds, string method, double ignoreAboveMs)
        {
            var list = new List<float>(ms.Length);
            foreach (var m in ms)
                if (ignoreAboveMs <= 0 || m <= ignoreAboveMs) list.Add(m);

            if (windowSeconds > 0)
            {
                double acc = 0;
                int start = list.Count;
                while (start > 0 && acc < windowSeconds * 1000.0) { start--; acc += list[start]; }
                list = list.GetRange(start, list.Count - start);
            }

            int n = list.Count;
            if (n == 0) return (null, null, null);
            double sum = 0;
            foreach (var m in list) sum += m;
            double avg = n * 1000.0 / sum;

            var worst = list.ToArray();
            Array.Sort(worst);
            Array.Reverse(worst);

            double? Low(double frac, int minFrames)
            {
                if (n < minFrames) return null;
                int k = Math.Max(1, (int)(n * frac));
                if (method == "average")
                {
                    double s = 0;
                    for (int i = 0; i < k; i++) s += worst[i];
                    return 1000.0 / (s / k);
                }
                return 1000.0 / worst[k - 1];
            }

            return (avg, Low(0.01, 100), Low(0.001, 1000));
        }
    }

    /// <summary>Descobre a API gráfica pelos módulos carregados no processo.</summary>
    public static class ApiDetector
    {
        static readonly ConcurrentDictionary<string, string> Cache = new ConcurrentDictionary<string, string>();

        public static string Detect(int pid, string runtime)
        {
            string rt = (runtime ?? "").ToUpperInvariant();
            string key = pid + "|" + rt;
            if (Cache.TryGetValue(key, out var v)) return v;
            string fallback = rt == "DXGI" ? "DirectX" : rt == "D3D9" ? "DX9" : "";
            Cache[key] = fallback;
            Task.Run(() =>
            {
                try
                {
                    var mods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    using (var p = Process.GetProcessById(pid))
                        foreach (ProcessModule m in p.Modules) mods.Add(m.ModuleName);
                    string api =
                        rt == "D3D9" ? "DX9" :
                        mods.Contains("vulkan-1.dll") ? "Vulkan" :
                        mods.Contains("d3d12.dll") ? "DX12" :
                        mods.Contains("d3d11.dll") ? "DX11" :
                        (mods.Contains("d3d10.dll") || mods.Contains("d3d10_1.dll")) ? "DX10" :
                        (mods.Contains("opengl32.dll") && rt != "DXGI") ? "OpenGL" : fallback;
                    Cache[key] = api;
                }
                catch { /* processo protegido (anti-cheat): fica o fallback */ }
            });
            return fallback;
        }
    }
}
