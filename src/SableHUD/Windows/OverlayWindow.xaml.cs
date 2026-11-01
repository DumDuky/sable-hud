using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SableHUD.Controls;
using SableHUD.Core;
using SableHUD.Input;
using SableHUD.Shared;

namespace SableHUD.Windows;

public partial class OverlayWindow : Window
{
    private IReadOnlyList<IHudPage> _pages;
    private readonly Dictionary<string, HudButton> _tabs = new();
    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _idle;
    private readonly DispatcherTimer _status;
    private readonly DispatcherTimer _refresh;
    private int _index;
    private bool _shown;
    private bool _prewarmed;
    private int _generation;
    private string _defaultTab = "Music";

    public event Action? Shown;
    public event Action? Hidden;

    public bool IsShown => _shown;
    public IHudPage? CurrentPage => _index >= 0 && _index < _pages.Count ? _pages[_index] : null;

    public OverlayWindow(IReadOnlyList<IHudPage> pages, string defaultTab)
    {
        InitializeComponent();
        _pages = pages;
        _defaultTab = pages.Any(p => p.TabId == defaultTab) ? defaultTab : pages[0].TabId;

        BuildTabs();
        BuildHints(_defaultTab);
        CloseButton.Click += (_, _) => Hud.Hide();
        PreviewMouseDown += (_, _) => ResetIdle();
        MouseWheel += (_, _) => ResetIdle();

        _clock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) =>
        {
            ClockText.Text = DateTime.Now.ToString("HH:mm");
            SetPad(Services.Pad.Pad);
        };
        _clock.Start();

        _idle = new DispatcherTimer(DispatcherPriority.Background);
        _idle.Tick += (_, _) =>
        {
            _idle.Stop();
            if (_shown) HideHud();
        };

        _refresh = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _refresh.Tick += (_, _) =>
        {
            if (_shown) CurrentPage?.Refresh();
        };
        _refresh.Start();

        _status = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
        _status.Tick += (_, _) =>
        {
            _status.Stop();
            StatusText.Visibility = Visibility.Collapsed;
        };

        ClockText.Text = DateTime.Now.ToString("HH:mm");
        VersionText.Text = "Sable " + (typeof(OverlayWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0");
        SetPad(Services.Pad.Pad);
        SetHotkeyHint(Services.Settings.KeyboardHotkey);
        SelectPage(_defaultTab, activate: false);
    }

    private void BuildTabs()
    {
        TabStrip.Children.Clear();
        _tabs.Clear();
        foreach (var page in _pages)
        {
            var (title, icon) = page.TabId switch
            {
                "Music" => ("Music", "IconMusic"),
                "Performance" => ("Perf", "IconPerf"),
                "Audio" => ("Audio", "IconVolume"),
                _ => ("Tools", "IconGrid"),
            };

            var button = new HudButton
            {
                Height = 28,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 0, 11, 0),
                FontSize = 11,
                Icon = Ui.Geo(icon),
                IconSize = 13,
                IconMargin = new Thickness(0, 0, 6, 0),
                Content = title,
                Margin = new Thickness(0, 0, 2, 0),
            };
            button.Click += (_, _) =>
            {
                SelectPage(page.TabId);
                ResetIdle();
            };
            _tabs[page.TabId] = button;
            TabStrip.Children.Add(button);
        }
    }

    private void BuildHints(string tabId)
    {
        HintStrip.Children.Clear();
        if (tabId != "Performance")
            AddHint("A", tabId == "Audio" ? "Mute" : tabId == "Tools" ? "Open" : "Select");
        if (tabId == "Music") AddHint("X", "Search");
        if (tabId == "Music") AddHint("LT/RT", "Seek");
        if (tabId == "Audio") AddHint("X", "Output");
        if (tabId == "Audio") AddHint("◀ ▶", "Volume");
        AddHint("LB/RB", "Tabs");
        AddHint("B", "Close");
        AddHint("Y", "Mini");
        AddHint("Menu", "Settings");
    }

    private void AddHint(string glyph, string label)
    {
        var chip = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderBrush = (Brush)FindResource("StrokeStrong"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        chip.Child = new TextBlock
        {
            Text = glyph,
            FontSize = 8.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextMuted"),
        };
        var text = new TextBlock
        {
            Text = label,
            FontSize = 9.5,
            Foreground = (Brush)FindResource("TextMuted"),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        HintStrip.Children.Add(chip);
        HintStrip.Children.Add(text);
    }

    public void SetPad(PadInfo info)
    {
        Dispatcher.BeginInvoke(() =>
        {
            PadDot.Fill = (Brush)FindResource(info.Connected ? "Text" : "TextDim");
            PadLabel.Text = !info.Connected
                ? "No pad · Ctrl+Alt+H"
                : info.Battery switch
                {
                    BatteryLevel.Wired => "Pad 1 · USB",
                    BatteryLevel.Full => "Pad 1 · full",
                    BatteryLevel.Medium => "Pad 1 · medium",
                    BatteryLevel.Low => "Pad 1 · low",
                    BatteryLevel.Empty => "Pad 1 · empty",
                    _ => "Pad 1",
                };
        });
    }

    public void SetHotkeyHint(bool enabled) => HotkeyHint.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

    public void PrepareRender()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        Visibility = Visibility.Visible;
        PageHost.BeginAnimation(OpacityProperty, null);
        PageHost.Opacity = 1;
        if (PageHost.RenderTransform is TranslateTransform slide)
            slide.BeginAnimation(TranslateTransform.YProperty, null);
        UpdateLayout();
    }

    public void SetStatus(string text)
    {
        if (!_shown) return;
        StatusText.Text = text;
        StatusText.Visibility = Visibility.Visible;
        _status.Stop();
        _status.Start();
        ResetIdle();
    }

    public void ScaleTo(double scale)
    {
        Width = 940 * scale;
        Height = 404 * scale;
        Root.LayoutTransform = new ScaleTransform(scale, scale);
        if (_shown) PositionNow();
    }

    private void PositionNow()
    {
        var wa = WindowFx.PrimaryWorkArea();
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top + 14;
    }

    public void Prewarm()
    {
        if (_prewarmed) return;
        _prewarmed = true;
        Left = -40000;
        Top = -40000;
        Opacity = 0;
        Show();
        UpdateLayout();
        HideImmediate();
    }

    public void ShowHud(string? tab = null)
    {
        if (tab is not null && _pages.Any(p => p.TabId.Equals(tab, StringComparison.OrdinalIgnoreCase)))
            SelectPage(tab, activate: false);
        else if (PageHost.Content is null)
            SelectPage(_defaultTab, activate: false);

        if (_shown)
        {
            ResetIdle();
            CurrentPage?.Activate();
            return;
        }

        var gen = ++_generation;
        PositionNow();
        var target = Top;
        var start = target - Height;
        Visibility = Visibility.Visible;
        Opacity = 0;
        Top = start;

        Shell.RenderTransformOrigin = new Point(0.5, 0);
        var shellScale = new ScaleTransform(0.982, 0.982);
        Shell.RenderTransform = shellScale;
        shellScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.982, 1, TimeSpan.FromMilliseconds(460))
        {
            EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut },
        });
        shellScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.982, 1, TimeSpan.FromMilliseconds(460))
        {
            EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut },
        });

        var drop = new DoubleAnimation(start, target, TimeSpan.FromMilliseconds(460))
        {
            EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut },
        };
        drop.Completed += (_, _) =>
        {
            if (gen != _generation || !_shown) return;
            BeginAnimation(TopProperty, null);
            Top = target;
        };
        BeginAnimation(TopProperty, drop);

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280));
        fade.Completed += (_, _) =>
        {
            if (gen != _generation || !_shown) return;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
        };
        BeginAnimation(OpacityProperty, fade);

        _shown = true;
        _prewarmed = true;
        CurrentPage?.Activate();
        Shown?.Invoke();
        ResetIdle();
    }

    public void HideHud()
    {
        if (!_shown) return;
        _generation++;
        var gen = _generation;
        _shown = false;
        _idle.Stop();

        var fromTop = Top;
        var target = fromTop - 46;
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            if (gen != _generation || _shown) return;
            BeginAnimation(TopProperty, null);
            BeginAnimation(OpacityProperty, null);
            Top = target;
            Opacity = 0;
            Visibility = Visibility.Hidden;
            CurrentPage?.Deactivate();
            Hidden?.Invoke();
        };
        BeginAnimation(OpacityProperty, fade);
        BeginAnimation(TopProperty, new DoubleAnimation(fromTop, target, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        });
    }

    public void HideImmediate()
    {
        var wasShown = _shown;
        _generation++;
        _shown = false;
        _idle.Stop();
        BeginAnimation(TopProperty, null);
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Visibility = Visibility.Hidden;
        CurrentPage?.Deactivate();
        if (wasShown) Hidden?.Invoke();
    }

    public void SelectPage(string? tabId, bool activate = true)
    {
        if (tabId is null) return;
        var index = _pages.ToList().FindIndex(p => p.TabId.Equals(tabId, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;

        if (index == _index && PageHost.Content is not null)
        {
            if (activate && _shown) CurrentPage?.Activate();
            return;
        }

        CurrentPage?.Deactivate();
        _index = index;
        var page = _pages[index];
        PageHost.Content = page as FrameworkElement;
        foreach (var (key, button) in _tabs) button.IsActive = key == page.TabId;
        BuildHints(page.TabId);

        PageHost.Opacity = 0;
        var slide = new TranslateTransform(0, 7);
        PageHost.RenderTransform = slide;
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(230)));
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(9, 0, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });

        if (activate && _shown) page.Activate();
    }

    public void SetDefaultTab(string tab)
    {
        if (_pages.Any(p => p.TabId.Equals(tab, StringComparison.OrdinalIgnoreCase)))
            _defaultTab = tab;
    }

    public void ConfigurePages(IReadOnlyList<IHudPage> pages, string defaultTab)
    {
        if (pages.Count == 0) return;
        _pages = pages;
        _defaultTab = pages.Any(p => p.TabId.Equals(defaultTab, StringComparison.OrdinalIgnoreCase))
            ? defaultTab
            : pages[0].TabId;

        var currentId = (PageHost.Content as IHudPage)?.TabId;
        var index = currentId is null
            ? -1
            : pages.ToList().FindIndex(p => p.TabId.Equals(currentId, StringComparison.OrdinalIgnoreCase));

        BuildTabs();

        if (index < 0)
        {
            SelectPage(_defaultTab, activate: _shown);
            return;
        }

        _index = index;
        foreach (var (key, button) in _tabs) button.IsActive = key == currentId;
        BuildHints(CurrentPage?.TabId ?? _defaultTab);
        if (_shown) CurrentPage?.Activate();
    }

    public void CyclePage(int delta)
    {
        if (_pages.Count == 0) return;
        var next = (_index + delta + _pages.Count) % _pages.Count;
        SelectPage(_pages[next].TabId);
        ResetIdle();
    }

    public void RefreshPage() => CurrentPage?.Refresh();

    public void Navigate(NavDir dir)
    {
        if (!_shown) return;
        ResetIdle();
        CurrentPage?.Navigate(dir);
    }

    public void Adjust(int direction)
    {
        if (!_shown) return;
        ResetIdle();
        CurrentPage?.Adjust(direction);
    }

    public void Confirm()
    {
        if (!_shown) return;
        ResetIdle();
        CurrentPage?.Confirm();
    }

    public bool Back()
    {
        if (!_shown) return false;
        ResetIdle();
        return CurrentPage?.Back() == true;
    }

    public void Secondary()
    {
        if (!_shown) return;
        ResetIdle();
        CurrentPage?.Secondary();
    }

    public void ResetIdle()
    {
        if (!_shown) return;
        _idle.Stop();
        var ms = Services.Settings.AutoHideMs;
        if (ms <= 0) return;
        _idle.Interval = TimeSpan.FromMilliseconds(ms);
        _idle.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowFx.MakeOverlay(this);
    }
}
