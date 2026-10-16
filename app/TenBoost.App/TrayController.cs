using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TenBoost.Core;

namespace TenBoost.App;

internal sealed class TrayController : IDisposable
{
    private readonly App _app;
    private readonly StatusHub _hub;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _toggle;
    private readonly ToolStripMenuItem _pick;
    private readonly Bitmap _face;
    private readonly Dictionary<Color, Icon> _icons = [];

    public TrayController(App app, StatusHub hub)
    {
        _app = app;
        _hub = hub;
        _face = LoadFace();
        _toggle = new ToolStripMenuItem("Включить", null, async (_, _) => await _hub.ToggleAsync()) { Enabled = false };
        _pick = new ToolStripMenuItem("Подобрать настройку", null, async (_, _) => await _hub.AutopickAsync()) { Enabled = false };
        var open = new ToolStripMenuItem("Открыть TenBoost", null, (_, _) => _app.ShowMain());
        open.Font = new Font(open.Font, System.Drawing.FontStyle.Bold);
        var menu = new ContextMenuStrip();
        menu.Items.Add(open);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_toggle);
        menu.Items.Add(_pick);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("О программе", null, (_, _) => _app.ShowAbout()));
        menu.Items.Add(new ToolStripMenuItem("Выход", null, (_, _) => _app.Shutdown()));

        _icon = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _app.ShowMain();
            }
        };
        _hub.Changed += Apply;
        Apply();
    }

    public void Dispose()
    {
        _hub.Changed -= Apply;
        _icon.Visible = false;
        _icon.Dispose();
        _face.Dispose();
        foreach (var icon in _icons.Values)
        {
            icon.Dispose();
        }
    }

    private void Apply()
    {
        var status = _hub.Last;
        var view = StatusView.From(status);
        _icon.Icon = IconFor(view.Dot);
        var tooltip = "TenBoost — " + view.Short;
        _icon.Text = tooltip.Length > 127 ? tooltip[..124] + "..." : tooltip;
        _toggle.Text = status is { Enabled: true } ? "Выключить" : "Включить";
        // Во время подбора обе команды недоступны: служба занята перебором.
        _toggle.Enabled = status is not null && !_hub.Busy && !_hub.Picking;
        _pick.Enabled = status is not null && !_hub.Busy && !_hub.Picking;
    }

    private static Bitmap LoadFace()
    {
        var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/tray.png"));
        using var stream = info.Stream;
        return new Bitmap(stream);
    }

    /// <summary>Морда Фера и точка состояния в правом нижнем углу: лайм — работает, серый — выключено.</summary>
    private Icon IconFor(Color dot)
    {
        if (_icons.TryGetValue(dot, out var cached))
        {
            return cached;
        }

        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_face, 0, 0, 32, 32);
            using var ring = new SolidBrush(Color.FromArgb(0x0C, 0x10, 0x0E));
            g.FillEllipse(ring, 18, 18, 14, 14);
            using var brush = new SolidBrush(dot);
            g.FillEllipse(brush, 20, 20, 10, 10);
        }

        var icon = Icon.FromHandle(bitmap.GetHicon());
        _icons[dot] = icon;
        return icon;
    }
}
