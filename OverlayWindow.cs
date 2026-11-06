using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace FpsOverlay
{
    /// <summary>Janela transparente, sempre por cima, que deixa o mouse passar.</summary>
    public sealed class OverlayWindow : Window
    {
        readonly AppSettings _set;
        readonly Engine _eng;
        public readonly OverlayView View;
        IntPtr _hwnd;
        double _lastHeight;
        readonly DispatcherTimer _topmost;

        public bool EditMode { get; private set; }
        public event Action EditModeChanged;

        public OverlayWindow(AppSettings set, Engine eng)
        {
            _set = set; _eng = eng;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Focusable = false;
            Title = "FPS Overlay";
            View = new OverlayView(set, eng, this);
            Content = View;

            _eng.Frame += OnFrame;
            _eng.Updated += () => { if (Math.Abs(View.LogicalHeight() - _lastHeight) > 0.1) ApplyGeometry(); };

            _topmost = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _topmost.Tick += (s, e) => ReassertTopmost();
            _topmost.Start();

            MouseLeftButtonDown += OnMouseDown;
            ApplyGeometry();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.SetExStyle(_hwnd, clickThrough: !EditMode);
            ReassertTopmost();
        }

        void OnFrame()
        {
            if (IsVisible) View.InvalidateVisual();
        }

        void ReassertTopmost()
        {
            if (_hwnd != IntPtr.Zero && IsVisible)
                Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOSIZE_NOMOVE_NOACTIVATE);
        }

        public void ApplyGeometry()
        {
            double s = Math.Clamp(_set.Scale, 0.4, 3.0);
            _lastHeight = View.LogicalHeight();
            double w = OverlayView.BaseW * s, h = _lastHeight * s;
            Width = w;
            Height = h;
            double sw = SystemParameters.PrimaryScreenWidth, sh = SystemParameters.PrimaryScreenHeight, m = _set.Margin;
            switch (_set.Anchor)
            {
                case "TopRight": Left = sw - w - m; Top = m; break;
                case "BottomLeft": Left = m; Top = sh - h - m; break;
                case "BottomRight": Left = sw - w - m; Top = sh - h - m; break;
                case "Custom": Left = _set.X; Top = _set.Y; break;
                default: Left = m; Top = m; break;
            }
            View.InvalidateVisual();
        }

        public void SetEditMode(bool on)
        {
            EditMode = on;
            if (_hwnd != IntPtr.Zero) Native.SetExStyle(_hwnd, clickThrough: !on);
            if (on && !IsVisible) Show();
            if (!on)
            {
                _set.Anchor = "Custom";
                _set.X = Left;
                _set.Y = Top;
                _set.Save();
            }
            ApplyGeometry();
            EditModeChanged?.Invoke();
        }

        void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!EditMode) return;
            if (e.ClickCount == 2) { SetEditMode(false); return; }
            try { DragMove(); } catch { }
            _set.Anchor = "Custom";
            _set.X = Left;
            _set.Y = Top;
        }
    }

    /// <summary>Desenha o overlay (mesmo layout do Afterburner/RTSS).</summary>
    public sealed class OverlayView : FrameworkElement
    {
        public const double BaseW = 224;
        const double Pad = 9;

        readonly AppSettings _set;
        readonly Engine _eng;
        readonly OverlayWindow _win;

        static readonly Brush White = Frozen(245, 245, 245);
        static readonly Brush Dim = Frozen(175, 175, 175);
        static readonly Brush Green = Frozen(118, 215, 46);
        static readonly Brush Warn = Frozen(255, 90, 80);
        static readonly Pen SepPen = FrozenPen(Color.FromArgb(40, 255, 255, 255), 1);
        static readonly Pen BasePen = FrozenPen(Color.FromArgb(28, 255, 255, 255), 1);
        static readonly Pen GraphPen = FrozenPen(Color.FromRgb(118, 215, 46), 1.3);
        static readonly Pen EditPen = MakeEditPen();
        static readonly FontFamily Family = new FontFamily("Bahnschrift, Segoe UI");
        readonly Dictionary<int, Typeface> _faces = new Dictionary<int, Typeface>();
        double _dip = 1;

        public OverlayView(AppSettings set, Engine eng, OverlayWindow win)
        {
            _set = set; _eng = eng; _win = win;
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
            IsHitTestVisible = true;
        }

        static Brush Frozen(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        static Pen FrozenPen(Color c, double w)
        {
            var p = new Pen(new SolidColorBrush(c), w);
            p.Freeze();
            return p;
        }

        static Pen MakeEditPen()
        {
            var p = new Pen(new SolidColorBrush(Color.FromRgb(118, 215, 46)), 1.5) { DashStyle = DashStyles.Dash };
            p.Freeze();
            return p;
        }

        Typeface Face(FontWeight w)
        {
            int key = w.ToOpenTypeWeight();
            if (!_faces.TryGetValue(key, out var f))
                _faces[key] = f = new Typeface(Family, FontStyles.Normal, w, FontStretches.SemiCondensed);
            return f;
        }

        FormattedText Ft(string s, double px, Brush b, FontWeight w) =>
            new FormattedText(s ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face(w), px, b, _dip);

        double Text(DrawingContext dc, double x, double baseline, string s, double px, Brush b, FontWeight w, bool right = false)
        {
            var ft = Ft(s, px, b, w);
            double width = ft.WidthIncludingTrailingWhitespace;
            dc.DrawText(ft, new Point(right ? x - width : x, baseline - ft.Baseline));
            return width;
        }

        string StatusText()
        {
            if (_win.EditMode) return "MODO MOVER: arraste. Ctrl+Shift+F12 trava";
            return _eng.Status ?? "";
        }

        public double LogicalHeight()
        {
            double h = Pad + 52;
            if (_set.ShowGraph) h += 26;
            if (_set.ShowGpu) h += 70;
            if (_set.ShowCpu) h += 70;
            if (_set.ShowRam) h += 24;
            if (!string.IsNullOrEmpty(StatusText())) h += 16;
            return h + Pad - 2;
        }

        static string F(double? v, int digits = 0) =>
            v.HasValue ? v.Value.ToString(digits == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) : "--";

        protected override void OnRender(DrawingContext dc)
        {
            _dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double s = Math.Clamp(_set.Scale, 0.4, 3.0);
            dc.PushTransform(new ScaleTransform(s, s));

            double W = BaseW, H = LogicalHeight();
            byte alpha = (byte)Math.Round(Math.Clamp(_set.Opacity, 0, 1) * 255);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(alpha, 12, 12, 12)), null, new Rect(0, 0, W, H), 6, 6);
            if (_win.EditMode)
                dc.DrawRoundedRectangle(null, EditPen, new Rect(1, 1, W - 2, H - 2), 6, 6);

            var hw = _eng.Hw ?? new HwSnapshot();
            double y = Pad;

            // ---- FPS + 1% / .1% / AVG
            string fps = F(_eng.CurFps);
            double lx = 146, rx = W - Pad;
            double fpsLabelW = Ft("FPS", 15, Green, FontWeights.Bold).Width;
            double px = 52;
            while (px > 28 && Ft(fps, px, White, FontWeights.Bold).Width + fpsLabelW + 8 > lx - Pad - 6) px -= 2;
            double wn = Text(dc, Pad - 1, y + 44, fps, px, White, FontWeights.Bold);
            Text(dc, Pad + wn + 4, y + 42, "FPS", 15, Green, FontWeights.Bold);
            var rows = new (string, double?)[] { ("1%", _eng.Low1), (".1%", _eng.Low01), ("AVG", _eng.Avg) };
            for (int i = 0; i < rows.Length; i++)
            {
                double b = y + 13 + i * 15;
                Text(dc, lx, b, rows[i].Item1, 12, Dim, FontWeights.SemiBold);
                Text(dc, rx, b, F(rows[i].Item2), 14, White, FontWeights.Bold, right: true);
            }
            y += 52;

            // ---- gráfico de frametime
            if (_set.ShowGraph)
            {
                double tw = Ft("000.0 ms", 12, White, FontWeights.SemiBold).Width;
                double gx0 = Pad, gx1 = W - Pad - tw - 6, gy0 = y + 2, gy1 = y + 22;
                Text(dc, W - Pad, y + 17, _eng.CurMs.HasValue ? F(_eng.CurMs, 1) + " ms" : "-- ms", 12, White, FontWeights.SemiBold, right: true);
                var data = _eng.Graph ?? Array.Empty<float>();
                dc.DrawLine(BasePen, new Point(gx0, gy1), new Point(gx1, gy1));
                if (data.Length >= 2)
                {
                    var sorted = (float[])data.Clone();
                    Array.Sort(sorted);
                    double med = sorted[sorted.Length / 2];
                    double top = Math.Max(Math.Max(med * 1.6, sorted[^1] * 1.05), 4.0);
                    int n = Math.Max(10, _set.GraphFrames);
                    double step = (gx1 - gx0) / Math.Max(1, n - 1);
                    double startX = gx1 - step * (data.Length - 1);
                    var geo = new StreamGeometry();
                    using (var ctx = geo.Open())
                    {
                        for (int i = 0; i < data.Length; i++)
                        {
                            var pt = new Point(startX + i * step, gy1 - (gy1 - gy0) * Math.Min(data[i], top) / top);
                            if (i == 0) ctx.BeginFigure(pt, false, false);
                            else ctx.LineTo(pt, true, false);
                        }
                    }
                    geo.Freeze();
                    dc.DrawGeometry(null, GraphPen, geo);
                }
                y += 26;
            }

            // ---- GPU
            if (_set.ShowGpu)
            {
                dc.DrawLine(SepPen, new Point(Pad, y + 3), new Point(W - Pad, y + 3));
                y += 8;
                Text(dc, Pad, y + 14, hw.GpuName, 15, Green, FontWeights.Bold);
                ThreeValues(dc, y + 39, hw.GpuLoad, hw.GpuTemp, hw.GpuPower);
                double w = Text(dc, Pad, y + 57, "VRAM", 12, Dim, FontWeights.SemiBold);
                Text(dc, Pad + w + 5, y + 57, F(hw.VramUsed) + "/" + F(hw.VramTotal) + " MB", 12, White, FontWeights.SemiBold);
                Text(dc, W - Pad, y + 57, F(hw.GpuClock) + " MHz", 12, White, FontWeights.SemiBold, right: true);
                y += 62;
            }

            // ---- CPU
            if (_set.ShowCpu)
            {
                dc.DrawLine(SepPen, new Point(Pad, y + 3), new Point(W - Pad, y + 3));
                y += 8;
                Text(dc, Pad, y + 14, hw.CpuName, 15, Green, FontWeights.Bold);
                ThreeValues(dc, y + 39, hw.CpuLoad, hw.CpuTemp, hw.CpuPower);
                double w = Text(dc, Pad, y + 57, "CLOCK", 12, Dim, FontWeights.SemiBold);
                Text(dc, Pad + w + 5, y + 57, F(hw.CpuClock) + " MHz", 12, White, FontWeights.SemiBold);
                y += 62;
            }

            // ---- RAM / API
            if (_set.ShowRam)
            {
                dc.DrawLine(SepPen, new Point(Pad, y + 3), new Point(W - Pad, y + 3));
                y += 8;
                double w = Text(dc, Pad, y + 13, "RAM", 12, Dim, FontWeights.SemiBold);
                Text(dc, Pad + w + 5, y + 13, F(hw.RamUsed) + "/" + F(hw.RamTotal) + " MB", 12, White, FontWeights.SemiBold);
                if (!string.IsNullOrEmpty(_eng.Api))
                    Text(dc, W - Pad, y + 13, _eng.Api, 12, White, FontWeights.Bold, right: true);
                y += 16;
            }

            string st = StatusText();
            if (!string.IsNullOrEmpty(st))
            {
                var ft = Ft(st, 10, _win.EditMode ? Green : Warn, FontWeights.SemiBold);
                ft.MaxTextWidth = W - 2 * Pad;
                ft.MaxLineCount = 1;
                ft.Trimming = TextTrimming.CharacterEllipsis;
                dc.DrawText(ft, new Point(Pad, y + 13 - ft.Baseline));
            }
            dc.Pop();
        }

        void ThreeValues(DrawingContext dc, double baseline, double? load, double? temp, double? power)
        {
            double col = (BaseW - 2 * Pad) / 3;
            ValueUnit(dc, Pad, baseline, F(load), "%");
            ValueUnit(dc, Pad + col, baseline, F(temp), "°C");
            ValueUnit(dc, Pad + 2 * col, baseline, F(power), "W");
        }

        void ValueUnit(DrawingContext dc, double x, double baseline, string val, string unit)
        {
            double w = Text(dc, x, baseline, val, 22, White, FontWeights.Bold);
            Text(dc, x + w + 2, baseline, unit, 13, Dim, FontWeights.SemiBold);
        }
    }
}
