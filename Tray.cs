using System;
using System.Windows.Forms;

namespace FpsOverlay
{
    /// <summary>Ícone na bandeja (perto do relógio).</summary>
    public sealed class Tray : IDisposable
    {
        readonly NotifyIcon _icon;
        readonly ToolStripMenuItem _overlayItem;
        readonly ToolStripMenuItem _moveItem;

        public Tray(App app)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Abrir painel", null, (s, e) => app.ShowMain());
            _overlayItem = new ToolStripMenuItem("Mostrar overlay", null, (s, e) => app.ToggleOverlay());
            menu.Items.Add(_overlayItem);
            _moveItem = new ToolStripMenuItem("Mover overlay", null, (s, e) => app.ToggleMove());
            menu.Items.Add(_moveItem);
            menu.Items.Add("Zerar estatísticas", null, (s, e) => App.Engine.ResetStats());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Sair", null, (s, e) => app.Quit());
            menu.Opening += (s, e) =>
            {
                _overlayItem.Checked = App.Settings.OverlayVisible;
                _moveItem.Checked = app.Overlay != null && app.Overlay.EditMode;
            };

            _icon = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "FPS Overlay",
                ContextMenuStrip = menu,
                Visible = true,
            };
            _icon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) app.ShowMain(); };
        }

        static System.Drawing.Icon LoadIcon()
        {
            try
            {
                var sri = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));
                if (sri != null)
                    using (sri.Stream)
                        return new System.Drawing.Icon(sri.Stream, SystemInformation.SmallIconSize);
            }
            catch { }
            return System.Drawing.SystemIcons.Application;
        }

        public void SetTip(string text)
        {
            if (text.Length > 63) text = text.Substring(0, 63);
            if (_icon.Text != text) _icon.Text = text;
        }

        public void Balloon(string title, string text) =>
            _icon.ShowBalloonTip(4000, title, text, ToolTipIcon.Info);

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
