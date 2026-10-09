using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Windows.Threading;
using TenBoost.Core;

namespace TenBoost.App;

internal sealed class TrayController : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly App _app;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _toggle;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<Color, Icon> _icons = [];
    private ControlResponse? _last;
    private AboutWindow? _about;
    private bool _busy;

    public TrayController(App app)
    {
        _app = app;
        _toggle = new ToolStripMenuItem("Включить", null, async (_, _) => await ToggleAsync()) { Enabled = false };
        var menu = new ContextMenuStrip();
        menu.Items.Add(_toggle);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("О программе", null, (_, _) => ShowAbout()));
        menu.Items.Add(new ToolStripMenuItem("Выход", null, (_, _) => _app.Shutdown()));

        _icon = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        Apply(null);

        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    public void Dispose()
    {
        _timer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        foreach (var icon in _icons.Values)
        {
            icon.Dispose();
        }
    }

    private async Task RefreshAsync()
    {
        if (_busy)
        {
            return;
        }

        Apply(await ControlClient.SendAsync(ControlCommand.Status));
    }

    private async Task ToggleAsync()
    {
        if (_last is null || _busy)
        {
            return;
        }

        _busy = true;
        _toggle.Enabled = false;
        try
        {
            Apply(await ControlClient.SendAsync(_last.Enabled ? ControlCommand.Disable : ControlCommand.Enable));
        }
        finally
        {
            _busy = false;
        }
    }

    private void Apply(ControlResponse? status)
    {
        _last = status;
        var (color, text) = status switch
        {
            null => (Color.Gray, "служба не отвечает"),
            { Enabled: false } => (Color.Gray, "выключено"),
            { State: EngineState.Running } => (Color.FromArgb(0x2E, 0xB8, 0x5C), "включено"),
            { State: EngineState.Starting } => (Color.FromArgb(0xF2, 0xA9, 0x00), "запускается"),
            { State: EngineState.Error } => (Color.FromArgb(0xD9, 0x3B, 0x3B), "ошибка: " + status.Message),
            _ => (Color.Gray, "выключено"),
        };

        _icon.Icon = IconFor(color);
        var tooltip = "TenBoost — " + text;
        _icon.Text = tooltip.Length > 127 ? tooltip[..124] + "..." : tooltip;
        _toggle.Text = status is { Enabled: true } ? "Выключить" : "Включить";
        _toggle.Enabled = status is not null && !_busy;
    }

    private Icon IconFor(Color color)
    {
        if (_icons.TryGetValue(color, out var cached))
        {
            return cached;
        }

        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, 28, 28);
            using var font = new Font(FontFamily.GenericSansSerif, 15, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("T", font, Brushes.White, new RectangleF(0, 1, 32, 32), format);
        }

        var icon = Icon.FromHandle(bitmap.GetHicon());
        _icons[color] = icon;
        return icon;
    }

    private void ShowAbout()
    {
        if (_about is { IsLoaded: true })
        {
            _about.Activate();
            return;
        }

        _about = new AboutWindow(_last?.EngineVersion);
        _about.Closed += (_, _) => _about = null;
        _about.Show();
        _about.Activate();
    }
}
