using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace FpsOverlay
{
    /// <summary>Junta frametimes + hardware e calcula FPS, 1%, 0.1% e média.</summary>
    public sealed class Engine
    {
        readonly AppSettings _set;
        readonly FrameStore _store;
        readonly PresentMonService _pm;
        readonly HardwareService _hw;
        readonly int _selfPid = Environment.ProcessId;
        DispatcherTimer _timer;
        long _lastStats, _lastLows;
        Task _lowsTask;

        public int? Target { get; private set; }
        public string TargetApp { get; private set; }
        public string Api { get; private set; } = "";
        public double? CurFps { get; private set; }
        public double? CurMs { get; private set; }
        public double? Avg { get; private set; }
        public double? Low1 { get; private set; }
        public double? Low01 { get; private set; }
        public float[] Graph { get; private set; } = Array.Empty<float>();
        public HwSnapshot Hw { get; private set; } = new HwSnapshot();
        public string Status => _pm.Status;

        /// <summary>~30x por segundo (redesenho do overlay).</summary>
        public event Action Frame;
        /// <summary>4x por segundo (números).</summary>
        public event Action Updated;

        public Engine(AppSettings set, FrameStore store, PresentMonService pm, HardwareService hw)
        {
            _set = set; _store = store; _pm = pm; _hw = hw;
        }

        public void Start()
        {
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        public void Stop() => _timer?.Stop();

        void OnTick(object sender, EventArgs e)
        {
            long now = Environment.TickCount64;
            if (now - _lastStats >= 250)
            {
                _lastStats = now;
                UpdateStats(now);
                Updated?.Invoke();
            }
            else if (Target is int t)
            {
                Graph = _store.Last(t, _set.GraphFrames);
            }
            Frame?.Invoke();
        }

        int? PickTarget(long now)
        {
            var procs = _store.List();
            var excl = new HashSet<string>(_set.ExcludeProcesses ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            bool Ok((int pid, string app, long last) p, long maxAge) =>
                p.app != null && now - p.last < maxAge && !excl.Contains(p.app) && p.pid != _selfPid;

            int? fg = Native.ForegroundPid();
            if (fg.HasValue)
            {
                var m = procs.FirstOrDefault(p => p.pid == fg.Value);
                if (Ok(m, 2000)) return fg.Value;
            }
            if (Target.HasValue)
            {
                var m = procs.FirstOrDefault(p => p.pid == Target.Value);
                if (Ok(m, 5000)) return Target.Value;
            }
            var best = procs.Where(p => Ok(p, 2000)).OrderByDescending(p => p.last).FirstOrDefault();
            if (best.app != null) return best.pid;
            return null;
        }

        void UpdateStats(long now)
        {
            var nt = PickTarget(now);
            if (nt != Target)
            {
                Target = nt;
                _lastLows = 0;
                Avg = Low1 = Low01 = null;
            }
            Hw = _hw.Snapshot;
            _store.Cleanup(Target);

            var info = Target.HasValue ? _store.Info(Target.Value) : null;
            if (!Target.HasValue || info == null)
            {
                CurFps = CurMs = null;
                Graph = Array.Empty<float>();
                TargetApp = null;
                Api = "";
                return;
            }
            int t = Target.Value;
            TargetApp = info.Value.app;
            Api = ApiDetector.Detect(t, info.Value.runtime);

            var recent = _store.Last(t, 4000);
            if (now - info.Value.last > 1500 || recent.Length == 0)
            {
                CurFps = CurMs = null;
            }
            else
            {
                double acc = 0;
                int n = 0;
                for (int i = recent.Length - 1; i >= 0; i--)
                {
                    acc += recent[i];
                    n++;
                    if (acc >= 1000) break;
                }
                CurFps = n * 1000.0 / acc;
                CurMs = acc / n;
            }
            int g = Math.Max(10, _set.GraphFrames);
            Graph = recent.Length > g ? recent[^g..] : recent;

            if (now - _lastLows >= 1000 && (_lowsTask == null || _lowsTask.IsCompleted))
            {
                _lastLows = now;
                var all = _store.All(t);
                int win = _set.StatsWindowSeconds;
                string method = _set.LowsMethod;
                double ignore = _set.IgnoreFramesAboveMs;
                _lowsTask = Task.Run(() => Stats.Compute(all, win, method, ignore))
                    .ContinueWith(r =>
                    {
                        if (r.Status == TaskStatus.RanToCompletion && Target == t)
                            (Avg, Low1, Low01) = r.Result;
                    }, TaskScheduler.FromCurrentSynchronizationContext());
            }
        }

        public void ResetStats()
        {
            if (Target.HasValue) _store.Reset(Target.Value);
            Avg = Low1 = Low01 = null;
            _lastLows = 0;
        }
    }
}
