using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SableHUD.Core;
using SableHUD.Media;
using SableHUD.Shared;

namespace SableHUD.Windows;

/// <summary>A quiet, click-through now-playing pebble that appears when the track changes.</summary>
public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _hold;
    private int _generation;

    public ToastWindow()
    {
        InitializeComponent();
        _hold = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.6) };
        _hold.Tick += (_, _) =>
        {
            _hold.Stop();
            FadeOut();
        };
    }

    public void ShowTrack(MediaSnapshot snap)
    {
        if (!snap.HasTrack) return;

        ToastLabel.Text = "Now playing";
        ToastSource.Text = snap.Source;
        ToastTitle.Text = snap.Title;
        ToastArtist.Text = string.IsNullOrWhiteSpace(snap.Artist) ? snap.Album : snap.Artist;
        ToastProgress.Value = snap.Progress;
        var art = Services.Cover.Pick(snap);
        ToastArt.ImageSource = art;
        ToastArtFallback.Visibility = art is null ? Visibility.Visible : Visibility.Collapsed;
        Present();
    }

    public void ShowMessage(string text)
    {
        ToastLabel.Text = "Sable";
        ToastSource.Text = "";
        ToastTitle.Text = text;
        ToastArtist.Text = "";
        ToastProgress.Value = 0;
        ToastArt.ImageSource = null;
        ToastArtFallback.Visibility = Visibility.Visible;
        Present();
    }

    private void Present()
    {
        _generation++;
        var wa = WindowFx.PrimaryWorkArea();
        Left = wa.Left + (wa.Width - Width) / 2;
        var target = wa.Top + 18;
        Top = target - 30;
        Visibility = Visibility.Visible;
        Opacity = 0;

        BeginAnimation(TopProperty, new DoubleAnimation(target - 30, target, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut },
        });
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));

        _hold.Stop();
        _hold.Start();
    }

    public void HideToast()
    {
        _generation++;
        _hold.Stop();
        BeginAnimation(TopProperty, null);
        BeginAnimation(OpacityProperty, null);
        Visibility = Visibility.Hidden;
        Opacity = 0;
    }

    public void PrepareRender()
    {
        _hold.Stop();
        _generation++;
        BeginAnimation(TopProperty, null);
        BeginAnimation(OpacityProperty, null);
        Visibility = Visibility.Visible;
        Opacity = 1;
        UpdateLayout();
    }

    private void FadeOut()
    {
        var gen = ++_generation;
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            if (gen != _generation) return;
            Visibility = Visibility.Hidden;
        };
        BeginAnimation(OpacityProperty, fade);
        BeginAnimation(TopProperty, new DoubleAnimation(Top, Top - 16, TimeSpan.FromMilliseconds(220)));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowFx.MakeOverlay(this, clickThrough: true);
    }
}
