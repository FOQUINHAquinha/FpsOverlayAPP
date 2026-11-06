using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FpsOverlay
{
    public sealed class AppSettings
    {
        // Overlay
        public bool OverlayVisible { get; set; } = true;
        public string Anchor { get; set; } = "TopLeft";   // TopLeft, TopRight, BottomLeft, BottomRight, Custom
        public double X { get; set; } = 20;
        public double Y { get; set; } = 20;
        public double Margin { get; set; } = 12;
        public double Scale { get; set; } = 1.0;
        public double Opacity { get; set; } = 0.72;
        public bool ShowGraph { get; set; } = true;
        public bool ShowGpu { get; set; } = true;
        public bool ShowCpu { get; set; } = true;
        public bool ShowRam { get; set; } = true;
        public int GraphFrames { get; set; } = 160;

        // Estatísticas
        public string LowsMethod { get; set; } = "percentile";   // percentile | average
        public int StatsWindowSeconds { get; set; } = 0;          // 0 = desde o início/reset
        public double IgnoreFramesAboveMs { get; set; } = 1000;

        // Hardware
        public string PreferredGpu { get; set; } = "";

        // Sistema
        public bool StartMinimized { get; set; } = false;
        public bool TrayHintShown { get; set; } = false;
        public string HotkeyToggle { get; set; } = "Ctrl+Shift+F10";
        public string HotkeyReset { get; set; } = "Ctrl+Shift+F11";
        public string HotkeyMove { get; set; } = "Ctrl+Shift+F12";
        public List<string> ExcludeProcesses { get; set; } = new List<string>
        {
            "dwm.exe", "explorer.exe", "ShellExperienceHost.exe", "SearchHost.exe",
            "StartMenuExperienceHost.exe", "TextInputHost.exe", "FPSOverlay.exe"
        };

        public static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FpsOverlay");

        static string FilePath => Path.Combine(Folder, "settings.json");

        static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions { WriteIndented = true };

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
            }
            catch { }
        }
    }
}
