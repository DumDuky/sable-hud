using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SableHUD.Controls;
using SableHUD.Core;
using SableHUD.Input;
using SableHUD.Shared;

namespace SableHUD.Widgets;

public partial class ToolsView : HudPageBase
{
    private enum Tool
    {
        Screenshot,
        Mic,
        Sound,
        GameBar,
        LiMusicLink,
        Controller,
    }

    private readonly List<TextBlock> _subs = new();
    private readonly List<HudButton> _tiles = new();

    public override string TabId => "Tools";

    public ToolsView()
    {
        InitializeComponent();
        Build();
        Refresh();
    }

    private void Build()
    {
        var defs = new (Tool Tool, string Title, string Sub, string Icon)[]
        {
            (Tool.Screenshot, "Screenshot", "Save the game, not the HUD", "IconCamera"),
            (Tool.Mic, "Microphone", "Mute or unmute", "IconMic"),
            (Tool.Sound, "Sound mixer", "Per-app output", "IconVolume"),
            (Tool.GameBar, "Game Bar", "Xbox shortcut", "IconMonitor"),
            (Tool.LiMusicLink, "LiMusic link", "Connect the HUD browser", "IconMusic"),
            (Tool.Controller, "Controller", "Isolation & layout", "IconGamepad"),
        };

        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        for (var i = 0; i < defs.Length; i++)
        {
            var def = defs[i];
            var column = (i % 3) switch { 0 => 0, 1 => 2, _ => 4 };
            var row = i < 3 ? 0 : 2;

            var button = new HudButton
            {
                Height = double.NaN,
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(20, 14, 20, 14),
                Background = (Brush)Application.Current.Resources["CardBg"],
                BorderBrush = (Brush)Application.Current.Resources["Stroke"],
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            };
            Grid.SetColumn(button, column);
            Grid.SetRow(button, row);

            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var iconBox = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(10),
                Background = (Brush)Application.Current.Resources["BgSoft"],
                BorderBrush = (Brush)Application.Current.Resources["Stroke"],
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            iconBox.Child = new System.Windows.Shapes.Path
            {
                Data = Ui.Geo(def.Icon),
                Fill = (Brush)Application.Current.Resources["Text"],
                Width = 17,
                Height = 17,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var title = new TextBlock
            {
                Text = def.Title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 0),
            };
            var sub = new TextBlock
            {
                Text = def.Sub,
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["TextDim"],
                TextTrimming = TextTrimming.CharacterEllipsis,
            };


            var bottom = new StackPanel();
            bottom.Children.Add(title);
            bottom.Children.Add(sub);
            content.Children.Add(iconBox);
            Grid.SetRow(bottom, 2);
            content.Children.Add(bottom);
            button.Content = content;

            var tool = def.Tool;
            button.Click += (_, _) => Run(tool);

            _tiles.Add(button);
            _subs.Add(sub);
            Register(button);
            Root.Children.Add(button);
        }

        SetCursorIndex(0);
    }

    private void Run(Tool tool)
    {
        switch (tool)
        {
            case Tool.Screenshot:
                var wasOpen = Hud.Visible;
                Hud.HideImmediate();
                var path = ScreenshotService.CaptureAllScreens();
                if (wasOpen) Hud.Show("Tools");
                Hud.Toast(path is null ? "Screenshot failed" : "Screenshot saved to Pictures\\Sable HUD");
                break;
            case Tool.Mic:
                Services.Audio.ToggleMicMute();
                Services.Audio.Refresh();
                Refresh();
                Hud.Toast(Services.Audio.Current.MicMuted ? "Microphone muted" : "Microphone live");
                break;
            case Tool.Sound:
                AppDetect.Open("ms-settings:apps-volume");
                Hud.Toast("Windows per-app sound mixer opened");
                break;
            case Tool.GameBar:
                AppDetect.DisableGameBarControllerButton();
                AppDetect.Open("ms-settings:gaming-gamebar");
                Hud.Toast("Game Bar Xbox shortcut off — sign out if it persists");
                break;
            case Tool.LiMusicLink:
                ConnectLiMusic();
                break;
            case Tool.Controller:
                Hud.OpenSettings("General");
                break;
        }
    }

    private async void ConnectLiMusic()
    {
        Hud.Toast("Connecting LiMusic…");
        var ok = await Services.Media.LiMusic.EnsureAsync(true);
        Hud.Toast(ok ? "LiMusic HUD link ready" : Services.Media.LiMusic.Status);
        Refresh();
    }

    public override void Refresh()
    {
        if (_subs.Count < 6) return;
        _subs[1].Text = Services.Audio.Current.MicMuted ? "Muted" : "Live";
        _subs[2].Text = "Windows mixer";
        _subs[3].Text = "Disable Xbox key";
        _subs[4].Text = Services.Media.LiMusic.State switch
        {
            Media.LiBridgeState.Connected => "Connected",
            Media.LiBridgeState.NeedsRestart => "Restart needed",
            Media.LiBridgeState.NotInstalled => "Not installed",
            Media.LiBridgeState.Error => "Link error",
            _ => "Offline",
        };
        _subs[5].Text = Services.Settings.IsolateController ? "Isolation on" : "Optional driver";
    }

    public override bool Navigate(NavDir dir)
    {
        if (Nav.Count == 0) return false;
        var i = Math.Max(0, CursorIndex);
        var row = i / 3;
        var col = i % 3;
        switch (dir)
        {
            case NavDir.Left:
                if (col > 0) SetCursorIndex(i - 1);
                return true;
            case NavDir.Right:
                if (col < 2) SetCursorIndex(i + 1);
                return true;
            case NavDir.Up:
                if (row > 0) SetCursorIndex(i - 3);
                return true;
            case NavDir.Down:
                if (row < 1) SetCursorIndex(i + 3);
                return true;
        }
        return false;
    }
}
