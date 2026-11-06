using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace FpsOverlay
{
    public partial class App : Application
    {
        public static AppSettings Settings { get; private set; }
        public static Engine Engine { get; private set; }
        public static HardwareService Hardware { get; private set; }

        public OverlayWindow Overlay { get; private set; }

        const string MutexName = "FpsOverlay_SingleInstance_v1";
        const string ShowEventName = "FpsOverlay_ShowPanel_v1";

        Mutex _mutex;
        EventWaitHandle _showEvent;
        FrameStore _store;
        PresentMonService _pm;
        HotkeyManager _hotkeys;
        Tray _tray;
        MainWindow _main;
        bool _quitting;

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, MutexName, out bool created);
            if (!created)
            {
                // já está aberto: só pede para mostrar o painel
                try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
                Shutdown();
                return;
            }

            base.OnStartup(e);
            DispatcherUnhandledException += OnUiException;
            AppDomain.CurrentDomain.UnhandledException += (s, a) => LogError(a.ExceptionObject as Exception);

            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            new Thread(() =>
            {
                while (true)
                {
                    _showEvent.WaitOne();
                    Dispatcher.BeginInvoke(new Action(ShowMain));
                }
            }) { IsBackground = true }.Start();

            Settings = AppSettings.Load();

            _store = new FrameStore();
            _pm = new PresentMonService(_store);
            _pm.Start();

            Hardware = new HardwareService { PreferredGpu = Settings.PreferredGpu ?? "" };
            Hardware.Start();

            Engine = new Engine(Settings, _store, _pm, Hardware);
            Overlay = new OverlayWindow(Settings, Engine);
            Engine.Start();
            if (Settings.OverlayVisible) Overlay.Show();

            _hotkeys = new HotkeyManager();
            _hotkeys.Register(Settings.HotkeyToggle, ToggleOverlay);
            _hotkeys.Register(Settings.HotkeyReset, () => Engine.ResetStats());
            _hotkeys.Register(Settings.HotkeyMove, ToggleMove);

            _tray = new Tray(this);
            Engine.Updated += () => _tray.SetTip("FPS Overlay: " + (Engine.TargetApp ?? "aguardando jogo"));

            bool minimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)) || Settings.StartMinimized;
            if (!minimized) ShowMain();
        }

        public void ShowMain()
        {
            if (_quitting) return;
            if (_main == null)
            {
                _main = new MainWindow();
                _main.Closed += (s, e) =>
                {
                    _main = null;
                    if (!_quitting && !Settings.TrayHintShown)
                    {
                        Settings.TrayHintShown = true;
                        Settings.Save();
                        _tray.Balloon("FPS Overlay continua rodando",
                            "O app está na bandeja, perto do relógio. Clique no ícone para abrir o painel.");
                    }
                };
            }
            _main.Show();
            if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
            _main.Activate();
        }

        public void ToggleOverlay() => SetOverlayVisible(!Settings.OverlayVisible);

        public void SetOverlayVisible(bool visible)
        {
            Settings.OverlayVisible = visible;
            Settings.Save();
            if (visible) Overlay.Show();
            else
            {
                if (Overlay.EditMode) Overlay.SetEditMode(false);
                Overlay.Hide();
            }
            OverlayStateChanged?.Invoke();
        }

        public void ToggleMove()
        {
            if (!Overlay.IsVisible) SetOverlayVisible(true);
            Overlay.SetEditMode(!Overlay.EditMode);
            OverlayStateChanged?.Invoke();
        }

        public event Action OverlayStateChanged;

        public void Quit()
        {
            if (_quitting) return;
            _quitting = true;
            try { if (Overlay.EditMode) Overlay.SetEditMode(false); } catch { }
            Settings.Save();
            try { Engine.Stop(); } catch { }
            try { _hotkeys?.Dispose(); } catch { }
            try { _tray?.Dispose(); } catch { }
            try { _pm?.Dispose(); } catch { }
            try { Hardware?.Dispose(); } catch { }
            try { _main?.Close(); } catch { }
            try { Overlay?.Close(); } catch { }
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { _pm?.Dispose(); } catch { }
            try { _tray?.Dispose(); } catch { }
            base.OnExit(e);
        }

        void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogError(e.Exception);
            e.Handled = true;
        }

        static void LogError(Exception ex)
        {
            if (ex == null) return;
            try
            {
                Directory.CreateDirectory(AppSettings.Folder);
                File.AppendAllText(Path.Combine(AppSettings.Folder, "erros.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
            }
            catch { }
        }
    }
}
