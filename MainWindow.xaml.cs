using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FpsOverlay
{
    public partial class MainWindow : Window
    {
        bool _loading = true;
        readonly App _app = (App)Application.Current;

        public MainWindow()
        {
            InitializeComponent();
            LoadValues();
            _loading = false;

            App.Engine.Updated += OnUpdated;
            _app.OverlayStateChanged += OnOverlayStateChanged;
            Closed += (s, e) =>
            {
                App.Engine.Updated -= OnUpdated;
                _app.OverlayStateChanged -= OnOverlayStateChanged;
            };
            OnUpdated();
        }

        AppSettings S => App.Settings;

        void LoadValues()
        {
            ShowOverlayCheck.IsChecked = S.OverlayVisible;
            SelectByTag(PositionCombo, S.Anchor);
            ScaleSlider.Value = S.Scale;
            OpacitySlider.Value = S.Opacity;
            ScaleText.Text = Pct(S.Scale);
            OpacityText.Text = Pct(S.Opacity);
            GraphCheck.IsChecked = S.ShowGraph;
            GpuCheck.IsChecked = S.ShowGpu;
            CpuCheck.IsChecked = S.ShowCpu;
            RamCheck.IsChecked = S.ShowRam;
            SelectByTag(LowsCombo, S.LowsMethod);
            SelectByTag(WindowCombo, S.StatsWindowSeconds.ToString(CultureInfo.InvariantCulture));
            StartupCheck.IsChecked = StartupManager.IsEnabled();
            MinimizedCheck.IsChecked = S.StartMinimized;
            HotkeysText.Text =
                $"Atalhos: {S.HotkeyToggle} mostra/oculta  ·  {S.HotkeyReset} zera estatísticas  ·  {S.HotkeyMove} mover overlay";
            OnOverlayStateChanged();
        }

        static string Pct(double v) => Math.Round(v * 100).ToString(CultureInfo.InvariantCulture) + "%";

        static void SelectByTag(ComboBox c, string tag)
        {
            foreach (var item in c.Items.OfType<ComboBoxItem>())
                if ((string)item.Tag == tag) { c.SelectedItem = item; return; }
            if (c.Items.Count > 0) c.SelectedIndex = 0;
        }

        static string SelectedTag(ComboBox c) => (c.SelectedItem as ComboBoxItem)?.Tag as string;

        static string F(double? v) => v.HasValue ? Math.Round(v.Value).ToString(CultureInfo.InvariantCulture) : "--";

        void OnOverlayStateChanged()
        {
            ShowOverlayCheck.IsChecked = S.OverlayVisible;
            MoveButton.Content = _app.Overlay != null && _app.Overlay.EditMode ? "Travar posição" : "Mover overlay";
            if (S.Anchor == "Custom")
            {
                bool prev = _loading;
                _loading = true;
                SelectByTag(PositionCombo, "Custom");
                _loading = prev;
            }
        }

        void OnUpdated()
        {
            var e = App.Engine;
            var hw = e.Hw ?? new HwSnapshot();

            FpsText.Text = F(e.CurFps);
            Low1Text.Text = F(e.Low1);
            Low01Text.Text = F(e.Low01);
            AvgText.Text = F(e.Avg);

            GameText.Text = e.TargetApp == null
                ? "Nenhum jogo detectado. Abra um jogo (de preferência em tela cheia sem bordas)."
                : $"Jogo: {e.TargetApp}" + (string.IsNullOrEmpty(e.Api) ? "" : $"   ·   API: {e.Api}");

            string st = e.Status;
            StatusText.Text = !string.IsNullOrEmpty(st) ? st
                : e.TargetApp != null ? "Rodando · medindo " + e.TargetApp
                : "Rodando · aguardando um jogo";

            // GPUs
            var names = hw.Gpus ?? new System.Collections.Generic.List<string>();
            var current = GpuCombo.Items.OfType<string>().ToList();
            if (!current.SequenceEqual(names))
            {
                _loading = true;
                GpuCombo.Items.Clear();
                foreach (var n in names) GpuCombo.Items.Add(n);
                string pref = S.PreferredGpu;
                if (!string.IsNullOrEmpty(pref) && names.Contains(pref)) GpuCombo.SelectedItem = pref;
                else GpuCombo.SelectedItem = names.FirstOrDefault(n => HardwareService.CleanGpuName(n) == hw.GpuName) ?? names.FirstOrDefault();
                _loading = false;
            }
            if (names.Count == 0 && hw.Ready) GpuCombo.IsEnabled = false; else GpuCombo.IsEnabled = true;

            GpuLiveText.Text = hw.Ready
                ? $"{F(hw.GpuLoad)}%   ·   {F(hw.GpuTemp)} °C   ·   {F(hw.GpuPower)} W   ·   VRAM {F(hw.VramUsed)}/{F(hw.VramTotal)} MB   ·   {F(hw.GpuClock)} MHz"
                : "Lendo sensores...";
            CpuNameText.Text = hw.CpuName;
            CpuLiveText.Text = hw.Ready
                ? $"{F(hw.CpuLoad)}%   ·   {F(hw.CpuTemp)} °C   ·   {F(hw.CpuPower)} W   ·   {F(hw.CpuClock)} MHz"
                : "Lendo sensores...";
            RamText.Text = $"{F(hw.RamUsed)} / {F(hw.RamTotal)} MB";
            PawnHint.Visibility = hw.Ready && hw.CpuFound && !hw.CpuTemp.HasValue ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------------- eventos

        void ApplyOverlay()
        {
            S.Save();
            _app.Overlay?.ApplyGeometry();
        }

        void ShowOverlayCheck_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _app.SetOverlayVisible(ShowOverlayCheck.IsChecked == true);
        }

        void PositionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var tag = SelectedTag(PositionCombo);
            if (tag == null) return;
            S.Anchor = tag;
            ApplyOverlay();
        }

        void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            S.Scale = Math.Round(e.NewValue, 2);
            ScaleText.Text = Pct(S.Scale);
            ApplyOverlay();
        }

        void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            S.Opacity = Math.Round(e.NewValue, 2);
            OpacityText.Text = Pct(S.Opacity);
            ApplyOverlay();
        }

        void SectionCheck_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            S.ShowGraph = GraphCheck.IsChecked == true;
            S.ShowGpu = GpuCheck.IsChecked == true;
            S.ShowCpu = CpuCheck.IsChecked == true;
            S.ShowRam = RamCheck.IsChecked == true;
            ApplyOverlay();
        }

        void MoveButton_Click(object sender, RoutedEventArgs e) => _app.ToggleMove();

        void ResetButton_Click(object sender, RoutedEventArgs e) => App.Engine.ResetStats();

        void GpuCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (GpuCombo.SelectedItem is string name)
            {
                S.PreferredGpu = name;
                App.Hardware.PreferredGpu = name;
                S.Save();
            }
        }

        void LowsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            S.LowsMethod = SelectedTag(LowsCombo) ?? "percentile";
            S.Save();
        }

        void WindowCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (int.TryParse(SelectedTag(WindowCombo), NumberStyles.Integer, CultureInfo.InvariantCulture, out int s))
            {
                S.StatsWindowSeconds = s;
                S.Save();
            }
        }

        void StartupCheck_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool want = StartupCheck.IsChecked == true;
            if (!StartupManager.Set(want))
            {
                MessageBox.Show(this, "Não foi possível alterar a inicialização com o Windows.", "FPS Overlay",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                StartupCheck.IsChecked = StartupManager.IsEnabled();
            }
        }

        void MinimizedCheck_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            S.StartMinimized = MinimizedCheck.IsChecked == true;
            S.Save();
        }

        void PawnButton_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo("https://pawnio.eu/") { UseShellExecute = true }); } catch { }
        }

        void QuitButton_Click(object sender, RoutedEventArgs e) => _app.Quit();
    }
}
