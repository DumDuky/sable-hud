using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SableHUD.Controls;
using SableHUD.Core;
using SableHUD.Media;
using SableHUD.Shared;

namespace SableHUD.Windows;

public partial class MiniPlayerWindow : Window
{
    private readonly DispatcherTimer _timer;
    private Point _dragStartScreen;
    private double _dragStartLeft;
    private double _dragStartTop;
    private bool _dragging;
    private bool _moved;
    private ImageSource? _art;

    public MiniPlayerWindow()
    {
        InitializeComponent();

        MiniPlay.Click += (_, _) => Services.Media.TogglePlayPause();
        MiniPrev.Click += (_, _) => Services.Media.Previous();
        MiniNext.Click += (_, _) => Services.Media.Next();

        PreviewMouseLeftButtonDown += OnDown;
        PreviewMouseMove += OnMove;
        PreviewMouseLeftButtonUp += OnUp;
        PreviewMouseLeftButtonDown += (_, _) => Refresh();

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(600) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        PositionInitial();
        Refresh();
    }

    public void Reposition() => PositionInitial();

    private void PositionInitial()
    {
        var wa = WindowFx.PrimaryWorkArea();
        var margin = 24.0;
        var dock = Services.Settings.MiniDock;
        double left;
        double top;

        switch (dock)
        {
            case "TopLeft":
                left = wa.Left + margin;
                top = wa.Top + margin;
                break;
            case "TopCenter":
                left = wa.Left + (wa.Width - Width) / 2;
                top = wa.Top + margin;
                break;
            case "TopRight":
                left = wa.Right - Width - margin;
                top = wa.Top + margin;
                break;
            case "MiddleLeft":
                left = wa.Left + margin;
                top = wa.Top + (wa.Height - Height) / 2;
                break;
            case "Center":
                left = wa.Left + (wa.Width - Width) / 2;
                top = wa.Top + (wa.Height - Height) / 2;
                break;
            case "MiddleRight":
                left = wa.Right - Width - margin;
                top = wa.Top + (wa.Height - Height) / 2;
                break;
            case "BottomLeft":
                left = wa.Left + margin;
                top = wa.Bottom - Height - margin;
                break;
            case "BottomCenter":
                left = wa.Left + (wa.Width - Width) / 2;
                top = wa.Bottom - Height - margin;
                break;
            case "Custom":
                left = Services.Settings.MiniLeft;
                top = Services.Settings.MiniTop;
                if (!Services.Settings.MiniPlaced || left > wa.Right - 80 || top > wa.Bottom - 40)
                {
                    left = wa.Right - Width - margin;
                    top = wa.Bottom - Height - margin;
                }
                break;
            default:
                left = wa.Right - Width - margin;
                top = wa.Bottom - Height - margin;
                break;
        }

        Left = Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width);
        Top = Clamp(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height);
    }

    private static double Clamp(double v, double min, double max) => max <= min ? v : Math.Clamp(v, min, max);

    public void ShowMini()
    {
        if (!Services.Settings.ShowMiniPlayer) return;
        if (Visibility == Visibility.Visible && Opacity > 0.95)
        {
            PositionInitial();
            Refresh();
            return;
        }

        PositionInitial();
        Refresh();
        BeginAnimation(OpacityProperty, null);
        Visibility = Visibility.Visible;
        Opacity = 1;
    }

    public void HideMini()
    {
        BeginAnimation(OpacityProperty, null);
        Visibility = Visibility.Hidden;
        Opacity = 0;
    }

    public void PrepareRender()
    {
        Visibility = Visibility.Visible;
        Opacity = 1;
        UpdateLayout();
    }

    public void Refresh()
    {
        var snap = Services.Demo ? Services.DemoMedia ?? MediaSnapshot.Empty : Services.Media.Current;
        MiniTitle.Text = snap.HasTrack ? snap.Title : "Nothing playing";
        MiniArtist.Text = snap.HasTrack
            ? (string.IsNullOrWhiteSpace(snap.Artist) ? snap.Source : $"{snap.Artist} · {snap.Source}")
            : "Press the Xbox button";
        MiniProgress.Value = snap.HasTrack ? snap.Progress : 0;
        MiniPlay.Icon = Ui.Geo(snap.IsPlaying ? "IconPause" : "IconPlay");
        MiniPlay.IsEnabled = snap.HasTrack;
        MiniPrev.IsEnabled = snap.HasTrack;
        MiniNext.IsEnabled = snap.HasTrack;

        var art = Services.Demo ? snap.Artwork : Services.Cover.Pick(snap);
        if (art is null && Services.Demo && snap.HasTrack) art = Ui.DemoArt();
        if (ReferenceEquals(art, _art)) return;
        _art = art;
        MiniArt.ImageSource = art;
        MiniArtFallback.Visibility = art is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (FindButton(e.OriginalSource as DependencyObject) is not null) return;
        _dragging = true;
        _moved = false;
        _dragStartScreen = PointToScreen(e.GetPosition(this));
        _dragStartLeft = Left;
        _dragStartTop = Top;
        CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = PointToScreen(e.GetPosition(this));
        var dpi = VisualTreeHelper.GetDpi(this);
        var dx = (p.X - _dragStartScreen.X) / dpi.DpiScaleX;
        var dy = (p.Y - _dragStartScreen.Y) / dpi.DpiScaleY;
        if (Math.Abs(dx) + Math.Abs(dy) > 2) _moved = true;

        var wa = WindowFx.PrimaryWorkArea();
        var maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width;
        var maxTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height;
        Left = Clamp(_dragStartLeft + dx, SystemParameters.VirtualScreenLeft, maxLeft);
        Top = Clamp(_dragStartTop + dy, SystemParameters.VirtualScreenTop, maxTop);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        if (!_moved) return;

        Services.Settings.MiniLeft = Left;
        Services.Settings.MiniTop = Top;
        Services.Settings.MiniPlaced = true;
        Services.Settings.MiniDock = "Custom";
        Services.Save();
    }

    private static HudButton? FindButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is HudButton b) return b;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowFx.MakeOverlay(this);
    }
}
