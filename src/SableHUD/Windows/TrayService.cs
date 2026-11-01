using System.Drawing;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace SableHUD.Windows;

public sealed class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ContextMenuStrip _menu;
    private readonly Icon _image;

    public event Action? ShowHud;
    public event Action? ToggleMini;
    public event Action? OpenSettings;
    public event Action? Exit;

    public Func<bool>? MiniChecked { get; set; }
    public Func<bool>? AutostartChecked { get; set; }

    public TrayService()
    {
        _image = LoadIcon();
        _menu = new WinForms.ContextMenuStrip
        {
            ShowImageMargin = false,
            Font = new Font("Segoe UI", 9.5f),
        };

        var show = new WinForms.ToolStripMenuItem("Open Sable HUD") { Font = new System.Drawing.Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Bold) };
        show.Click += (_, _) => ShowHud?.Invoke();

        var mini = new WinForms.ToolStripMenuItem("Mini player");
        mini.Click += (_, _) => ToggleMini?.Invoke();

        var settings = new WinForms.ToolStripMenuItem("Settings…");
        settings.Click += (_, _) => OpenSettings?.Invoke();

        var autostart = new WinForms.ToolStripMenuItem("Start with Windows");
        autostart.Click += (_, _) => AutostartChecked?.Invoke();

        var exit = new WinForms.ToolStripMenuItem("Exit Sable");
        exit.Click += (_, _) => Exit?.Invoke();

        _menu.Items.Add(show);
        _menu.Items.Add(mini);
        _menu.Items.Add(settings);
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add(autostart);
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add(exit);

        _menu.Opening += (_, _) =>
        {
            mini.Checked = MiniChecked?.Invoke() ?? false;
            autostart.Checked = AutostartChecked?.Invoke() ?? false;
        };

        _icon = new WinForms.NotifyIcon
        {
            Icon = _image,
            Text = "Sable HUD",
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _icon.DoubleClick += (_, _) => ShowHud?.Invoke();
    }

    public void Balloon(string title, string text)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = text;
            _icon.BalloonTipIcon = WinForms.ToolTipIcon.None;
            _icon.ShowBalloonTip(4200);
        }
        catch { }
    }

    private static Icon LoadIcon()
    {
        try
        {
            var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))?.Stream;
            if (stream is not null)
            {
                using (stream)
                    return new Icon(stream, 32, 32);
            }
        }
        catch { }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        try
        {
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
            _image.Dispose();
        }
        catch { }
    }
}
