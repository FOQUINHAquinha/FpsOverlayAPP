using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using System.Windows.Interop;

namespace FpsOverlay
{
    /// <summary>Atalhos globais (funcionam com o jogo em foco).</summary>
    public sealed class HotkeyManager : IDisposable
    {
        readonly HwndSource _src;
        readonly Dictionary<int, Action> _map = new Dictionary<int, Action>();
        int _nextId = 1;

        public HotkeyManager()
        {
            var p = new HwndSourceParameters("FpsOverlayHotkeys")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            };
            _src = new HwndSource(p);
            _src.AddHook(Hook);
        }

        public bool Register(string combo, Action action)
        {
            if (!Parse(combo, out uint mods, out uint vk)) return false;
            int id = _nextId++;
            if (!Native.RegisterHotKey(_src.Handle, id, mods | 0x4000 /*NOREPEAT*/, vk)) return false;
            _map[id] = action;
            return true;
        }

        public void UnregisterAll()
        {
            foreach (var id in _map.Keys) Native.UnregisterHotKey(_src.Handle, id);
            _map.Clear();
        }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0312 && _map.TryGetValue(wParam.ToInt32(), out var a))
            {
                a();
                handled = true;
            }
            return IntPtr.Zero;
        }

        static readonly Dictionary<string, uint> Names = new Dictionary<string, uint>
        {
            ["insert"] = 0x2D, ["delete"] = 0x2E, ["home"] = 0x24, ["end"] = 0x23,
            ["pageup"] = 0x21, ["pagedown"] = 0x22, ["pause"] = 0x13, ["scrolllock"] = 0x91,
        };

        public static bool Parse(string s, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            foreach (var part in s.ToLowerInvariant().Replace(" ", "").Split('+'))
            {
                switch (part)
                {
                    case "ctrl": case "control": mods |= 0x2; break;
                    case "shift": mods |= 0x4; break;
                    case "alt": mods |= 0x1; break;
                    case "win": mods |= 0x8; break;
                    default:
                        if (part.Length > 1 && part[0] == 'f' && int.TryParse(part.Substring(1), out int f) && f >= 1 && f <= 24)
                            vk = (uint)(0x70 + f - 1);
                        else if (Names.TryGetValue(part, out uint n))
                            vk = n;
                        else if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
                            vk = char.ToUpperInvariant(part[0]);
                        break;
                }
            }
            return vk != 0;
        }

        public void Dispose()
        {
            UnregisterAll();
            _src.Dispose();
        }
    }

    /// <summary>Iniciar com o Windows via Agendador de Tarefas (necessário porque o app roda como admin).</summary>
    public static class StartupManager
    {
        const string TaskName = "FPS Overlay";

        static int Schtasks(params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit(10000);
                return p.ExitCode;
            }
            catch { return -1; }
        }

        public static bool IsEnabled() => Schtasks("/Query", "/TN", TaskName) == 0;

        public static bool Set(bool enabled)
        {
            if (!enabled) return Schtasks("/Delete", "/TN", TaskName, "/F") == 0;

            string exe = Environment.ProcessPath ?? "";
            string user = SecurityElement.Escape(Environment.UserDomainName + "\\" + Environment.UserName);
            string xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>Inicia o FPS Overlay ao entrar no Windows</Description></RegistrationInfo>
  <Triggers>
    <LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId><Delay>PT5S</Delay></LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author""><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec><Command>{SecurityElement.Escape(exe)}</Command><Arguments>--minimized</Arguments></Exec>
  </Actions>
</Task>";
            string tmp = Path.Combine(Path.GetTempPath(), "fpsoverlay_task.xml");
            File.WriteAllText(tmp, xml, Encoding.Unicode);
            try { return Schtasks("/Create", "/TN", TaskName, "/XML", tmp, "/F") == 0; }
            finally { try { File.Delete(tmp); } catch { } }
        }
    }
}
