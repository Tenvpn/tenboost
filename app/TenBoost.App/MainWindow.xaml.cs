using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TenBoost.Core;

namespace TenBoost.App;

/// <summary>
/// Главное окно: Фер за компом показывает состояние. Выключено — отдыхает, ноги на столе;
/// включено — капюшон на голове, стучит по клавиатуре; подбор — с лупой у экрана; ошибка — разводит лапами.
/// </summary>
public partial class MainWindow : Window
{
    public const string TenvpnUrl = "https://tenproxy.site/?utm_source=tenboost&utm_medium=app&utm_campaign=window";

    private static readonly Duration Fade = new(TimeSpan.FromMilliseconds(380));

    private readonly StatusHub _hub;
    private readonly Dictionary<string, BitmapImage> _frames = [];
    private readonly DispatcherTimer _typing = new();
    private readonly Random _random = new();
    private readonly Dictionary<string, string> _strategyTitles;
    private Image _front;
    private Image _back;
    private Scene? _scene;
    private bool _typingSecond;
    private int _burst;

    internal MainWindow(StatusHub hub)
    {
        InitializeComponent();
        _hub = hub;
        _front = LayerA;
        _back = LayerB;
        _strategyTitles = LoadStrategyTitles();
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        VersionText.Text = "версия " + version.Split('+')[0];

        _typing.Tick += (_, _) => TypeStep();
        _hub.Changed += Render;
        IsVisibleChanged += (_, _) =>
        {
            _hub.Watching = IsVisible;
            UpdateTyping();
        };
        Render();
    }

    /// <summary>Выход из программы, а не крестик: окно правда закрывается.</summary>
    internal bool Quitting { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!Quitting)
        {
            // Крестик прячет окно в трей: ускорение работает дальше.
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _hub.Changed -= Render;
        _typing.Stop();
        base.OnClosed(e);
    }

    private void Render()
    {
        var status = _hub.Last;
        var view = StatusView.From(status);

        BadgeText.Text = view.Badge;
        var (badgeBack, badgeFore) = view.Tone switch
        {
            Tone.Ok or Tone.Progress => ("AccentSoft", "Accent"),
            Tone.Warn => ("WarnSoft", "Warn"),
            Tone.Bad => ("DangerSoft", "Danger"),
            _ => ("Elevated", "Muted"),
        };
        Badge.Background = Res(badgeBack);
        BadgeText.Foreground = Res(badgeFore);
        BadgeDot.Fill = Res(badgeFore);

        TitleText.Text = view.Title;
        HintText.Text = view.Hint;
        DetailText.Text = view.Detail ?? "";
        DetailText.Visibility = string.IsNullOrEmpty(view.Detail) ? Visibility.Collapsed : Visibility.Visible;

        var pick = status?.Pick;
        PickBar.Visibility = _hub.Picking ? Visibility.Visible : Visibility.Collapsed;
        PickBar.Value = pick is { Total: > 0 } ? (pick.Done + 0.5) / pick.Total : 0.1;

        // Включено — первичной кнопки нет: лайм на экране уже говорит «работает».
        var enabled = status is { Enabled: true };
        ToggleButton.Content = enabled ? "Выключить" : "Включить";
        ToggleButton.Style = (Style)FindResource(enabled ? "OutlineButton" : "PrimaryButton");
        ToggleButton.Height = 50;
        var idle = status is not null && !_hub.Busy && !_hub.Picking;
        ToggleButton.IsEnabled = idle;
        PickButton.IsEnabled = idle;

        StrategyText.Text = status?.StrategyId is { } id
            ? "Настройка: " + (_strategyTitles.TryGetValue(id, out var title) ? title : id)
            : "";

        ShowScene(view.Scene);
    }

    private void ShowScene(Scene scene)
    {
        if (_scene == scene)
        {
            return;
        }

        _scene = scene;
        CrossfadeTo(FrameFor(scene));
        var glow = scene is Scene.Work or Scene.Search ? 1 : 0;
        Glow.BeginAnimation(OpacityProperty, new DoubleAnimation(glow, new Duration(TimeSpan.FromMilliseconds(700))));
        Breathe(scene is Scene.Off);
        UpdateTyping();
    }

    private static string FrameFor(Scene scene) => scene switch
    {
        Scene.Work => "on-work-1",
        Scene.Search => "search-1",
        Scene.Error => "error-1",
        _ => "off-relax-1",
    };

    private void CrossfadeTo(string frame)
    {
        _back.Source = Frame(frame);
        _back.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Fade));
        _front.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, Fade));
        (_front, _back) = (_back, _front);
        _typingSecond = false;
    }

    /// <summary>Пока отдыхает — еле заметно дышит.</summary>
    private void Breathe(bool on)
    {
        if (!on)
        {
            Breath.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            return;
        }

        Breath.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.012, new Duration(TimeSpan.FromSeconds(2.2)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    private void UpdateTyping()
    {
        if (_scene == Scene.Work && IsVisible)
        {
            _burst = 0;
            _typing.Interval = TimeSpan.FromMilliseconds(600);
            _typing.Start();
        }
        else
        {
            _typing.Stop();
        }
    }

    /// <summary>Печатает очередями: несколько ударов по клавишам, потом пауза, как живой.</summary>
    private void TypeStep()
    {
        _typingSecond = !_typingSecond;
        _front.Source = Frame(_typingSecond ? "on-work-2" : "on-work-1");
        if (_burst <= 0)
        {
            _burst = _random.Next(4, 12);
        }

        _burst--;
        _typing.Interval = _burst == 0 && !_typingSecond
            ? TimeSpan.FromMilliseconds(_random.Next(700, 1600))
            : TimeSpan.FromMilliseconds(_random.Next(110, 190));
    }

    private BitmapImage Frame(string name)
    {
        if (_frames.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri($"pack://application:,,,/Assets/Mascot/{name}.png");
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        _frames[name] = image;
        return image;
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    private static Dictionary<string, string> LoadStrategyTitles()
    {
        try
        {
            return Catalog.Load(InstallLayout.ForCurrentProcess().CatalogFile).Strategies.ToDictionary(s => s.Id, s => s.Title);
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or CatalogException or System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnHide(object sender, RoutedEventArgs e) => Hide();

    private async void OnToggle(object sender, RoutedEventArgs e) => await _hub.ToggleAsync();

    private async void OnPick(object sender, RoutedEventArgs e) => await _hub.AutopickAsync();

    private void OnTenvpn(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(TenvpnUrl) { UseShellExecute = true });

    private void OnAbout(object sender, RoutedEventArgs e) => ((App)Application.Current).ShowAbout();
}
