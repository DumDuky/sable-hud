using System.Diagnostics;
using System.Windows;
using SableHUD.Core;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SableHUD.Controls;
using SableHUD.Input;
using SableHUD.Shared;
using SableHUD.SystemInfo;

namespace SableHUD.Widgets;

public partial class AudioView : HudPageBase
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<HudButton> _sessionButtons = new();
    private long _lastRefresh = -10_000;
    private string _signature = "";

    private Grid? _outputPanel;
    private TextBlock? _outputTitle;
    private TextBlock? _outputStatus;
    private StackPanel? _outputRows;
    private readonly List<HudButton> _outputButtons = new();
    private readonly List<AudioDeviceInfo> _outputDevices = new();
    private bool _outputOpen;
    private int _outputIndex;
    private int _outputPid;
    private string _outputName = "";
    private string _outputCurrentDeviceId = "";

    public override string TabId => "Audio";
    public override bool KeepVisible => _outputOpen;

    public AudioView()
    {
        InitializeComponent();

        MasterButton.Click += (_, _) =>
        {
            Services.Audio.ToggleMasterMute();
            ForceRefresh();
            Hud.Toast(Services.Audio.Current.MasterMuted ? "System audio muted" : "System audio on");
        };
        MicButton.Click += (_, _) =>
        {
            Services.Audio.ToggleMicMute();
            ForceRefresh();
            Hud.Toast(Services.Audio.Current.MicMuted ? "Microphone muted" : "Microphone live");
        };
        OutputButton.Click += (_, _) =>
        {
            if (Current?.Tag is int currentPid)
            {
                var session = Services.Audio.Current.Sessions.FirstOrDefault(s => s.Pid == currentPid);
                if (session is not null)
                {
                    OpenOutputPicker(session.Pid, session.Name, session.DeviceId);
                    return;
                }
            }

            var first = Services.Audio.Current.Sessions.FirstOrDefault();
            if (first is null)
            {
                AppDetect.Open("ms-settings:apps-volume");
                Hud.Toast("No app sessions — opened Windows mixer");
                return;
            }

            OpenOutputPicker(first.Pid, first.Name, first.DeviceId);
        };

        BuildOutputPicker();
        Refresh();
    }

    public override void Activate()
    {
        try { ForceRefresh(); }
        catch (Exception ex) { Log.Warn("audio activate: " + ex.Message); }
        base.Activate();
    }

    public override void Refresh()
    {
        try
        {
            if (_clock.ElapsedMilliseconds - _lastRefresh > 1400) ForceRefresh();
            else UpdateUi(CurrentSnapshot());
        }
        catch (Exception ex)
        {
            Log.Warn("audio refresh: " + ex.Message);
        }
    }

    private void ForceRefresh()
    {
        _lastRefresh = _clock.ElapsedMilliseconds;
        var snapshot = Services.Demo ? Services.DemoAudio ?? AudioSnapshot.Empty : Services.Audio.Refresh();
        UpdateUi(snapshot);
    }

    private AudioSnapshot CurrentSnapshot() =>
        Services.Demo ? Services.DemoAudio ?? AudioSnapshot.Empty : Services.Audio.Current;

    private void UpdateUi(AudioSnapshot s)
    {
        MasterValue.Text = $"{s.Master * 100:0}";
        MasterBar.Value = s.Master;
        MasterState.Text = s.MasterMuted ? "muted" : "live";
        MasterHint.Text = "Click to mute · ◀ ▶ volume · " + s.DefaultDeviceName;
        MasterHint.ToolTip = s.DefaultDeviceName;
        MasterIcon.Data = Ui.Geo(s.MasterMuted ? "IconVolumeMute" : "IconVolume");
        MicButton.Content = s.MicAvailable ? (s.MicMuted ? "Mic muted" : "Mic live") : "No mic";
        MicButton.Icon = Ui.Geo(s.MicMuted ? "IconMicOff" : "IconMic");
        MicButton.IsEnabled = s.MicAvailable;

        var sig = string.Join("|", s.Sessions.Select(x => $"{x.Pid}:{x.Name}:{Math.Round(x.Volume * 100)}:{x.Muted}"));
        if (sig == _signature)
        {
            UpdateNumbers(s);
            return;
        }
        _signature = sig;
        BuildSessions(s);
    }

    private void UpdateNumbers(AudioSnapshot s)
    {
        for (var i = 0; i < _sessionButtons.Count && i < s.Sessions.Count; i++)
        {
            if (_sessionButtons[i].Content is Grid grid)
            {
                foreach (var child in grid.Children)
                {
                    if (child is HudProgress bar) bar.Value = s.Sessions[i].Volume;
                    else if (child is TextBlock tb && tb.Tag as string == "pct")
                        tb.Text = $"{s.Sessions[i].Volume * 100:0}";
                }
            }
        }
    }

    private void BuildSessions(AudioSnapshot s)
    {
        var current = CursorIndex;
        var currentPid = Current?.Tag as int? ?? 0;
        ClearNav();
        _sessionButtons.Clear();
        SessionRows.Children.Clear();

        var shown = s.Sessions.Take(3).ToList();
        NoSessions.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        Register(MasterButton);
        Register(OutputButton);
        Register(MicButton);

        foreach (var session in shown)
        {
            var button = new HudButton
            {
                Height = 34,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(9, 0, 9, 0),
                Margin = new Thickness(0, 0, 0, 3),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ToolTip = "A mutes · ◀ ▶ volume",
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(94) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });

            var name = new TextBlock
            {
                Text = session.Name,
                FontSize = 11.4,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Left,
                Foreground = (Brush)Application.Current.Resources[session.Muted ? "TextDim" : "Text"],
            };
            var device = new TextBlock
            {
                Text = session.DeviceName,
                FontSize = 8.4,
                Foreground = (Brush)Application.Current.Resources["TextDim"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 230,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
            nameStack.Children.Add(name);
            nameStack.Children.Add(device);
            var bar = new HudProgress
            {
                Height = 4,
                VerticalAlignment = VerticalAlignment.Center,
                Track = (Brush)Application.Current.Resources["StrokeStrong"],
                Fill = (Brush)Application.Current.Resources["TextMuted"],
                Value = session.Volume,
                Margin = new Thickness(10, 0, 10, 0),
            };
            var pct = new TextBlock
            {
                Tag = "pct",
                Text = $"{session.Volume * 100:0}",
                FontSize = 9.8,
                Foreground = (Brush)Application.Current.Resources["TextDim"],
                VerticalAlignment = VerticalAlignment.Center,
                Width = 26,
                TextAlignment = TextAlignment.Right,
            };
            var mute = new System.Windows.Shapes.Path
            {
                Data = Ui.Geo(session.Muted ? "IconVolumeMute" : "IconVolume"),
                Fill = (Brush)Application.Current.Resources[session.Muted ? "Text" : "TextDim"],
                Width = 13,
                Height = 13,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            Grid.SetColumn(bar, 1);
            Grid.SetColumn(pct, 2);
            Grid.SetColumn(mute, 3);
            grid.Children.Add(nameStack);
            grid.Children.Add(bar);
            grid.Children.Add(pct);
            grid.Children.Add(mute);
            button.Content = grid;
            button.Tag = session.Pid;

            var pid = session.Pid;
            button.Click += (_, _) =>
            {
                Services.Audio.ToggleSessionMute(pid);
                ForceRefresh();
                Hud.Toast($"{session.Name} toggled");
            };

            _sessionButtons.Add(button);
            SessionRows.Children.Add(button);
            Register(button);
        }

        if (currentPid != 0)
        {
            var match = _sessionButtons.FirstOrDefault(b => b.Tag as int? == currentPid);
            if (match is not null) { SetCursorIndex(Nav.IndexOf(match)); return; }
        }

        if (CursorIndex < 0 || CursorIndex >= Nav.Count)
            SetCursorIndex(Math.Clamp(current < 0 ? 0 : current, 0, Nav.Count - 1));
        else
            SetCursorIndex(CursorIndex);
    }

    private void BuildOutputPicker()
    {
        if (Content is not Grid root || _outputPanel is not null) return;

        var panel = new Grid { Visibility = Visibility.Collapsed };
        var card = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBg"],
            BorderBrush = (Brush)Application.Current.Resources["Stroke"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14, 12, 14, 10),
        };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var back = new HudButton
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Padding = new Thickness(0),
            Icon = Ui.Geo("IconBack"), IconSize = 13, IconMargin = new Thickness(0),
        };
        back.Click += (_, _) => CloseOutputPicker();

        _outputTitle = new TextBlock
        {
            FontSize = 17, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _outputStatus = new TextBlock
        {
            FontSize = 10.5,
            Foreground = (Brush)Application.Current.Resources["TextDim"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            MaxWidth = 330,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(back, 0);
        Grid.SetColumn(_outputTitle, 2);
        Grid.SetColumn(_outputStatus, 3);
        header.Children.Add(back);
        header.Children.Add(_outputTitle);
        header.Children.Add(_outputStatus);
        layout.Children.Add(header);

        _outputRows = new StackPanel();
        Grid.SetRow(_outputRows, 2);
        layout.Children.Add(_outputRows);

        var hint = new TextBlock
        {
            Text = "A routes this app · B back",
            FontSize = 9.5,
            Foreground = (Brush)Application.Current.Resources["TextDim"],
            Margin = new Thickness(2, 0, 0, 0),
        };
        Grid.SetRow(hint, 3);
        layout.Children.Add(hint);

        card.Child = layout;
        panel.Children.Add(card);
        Grid.SetRow(panel, 0);
        Grid.SetRowSpan(panel, 5);
        root.Children.Add(panel);
        _outputPanel = panel;
    }

    private void OpenOutputPicker(int pid, string name, string deviceId)
    {
        if (_outputPanel is null) return;
        _outputPid = pid;
        _outputName = name;
        _outputCurrentDeviceId = deviceId;
        _outputOpen = true;
        _outputPanel.Visibility = Visibility.Visible;
        _outputTitle!.Text = "Output · " + name;
        _outputDevices.Clear();
        _outputDevices.Add(new AudioDeviceInfo("", "System default", string.IsNullOrEmpty(deviceId)));
        _outputDevices.AddRange(Services.Audio.Current.Devices.Take(5));

        _outputIndex = Math.Max(0, _outputDevices.FindIndex(d => d.Id == deviceId));
        _outputStatus!.Text = Services.Audio.RoutingSupported
            ? "Route this app, not the whole PC"
            : "Windows build lacks per-app routing";
        RenderOutputRows();
    }

    private void CloseOutputPicker()
    {
        if (_outputPanel is null) return;
        _outputOpen = false;
        _outputPanel.Visibility = Visibility.Collapsed;
        _outputButtons.Clear();
    }

    private void RenderOutputRows()
    {
        if (_outputRows is null) return;
        _outputRows.Children.Clear();
        _outputButtons.Clear();
        var offset = Math.Clamp(_outputIndex - 1, 0, Math.Max(0, _outputDevices.Count - 5));
        for (var i = 0; i < 5 && offset + i < _outputDevices.Count; i++)
        {
            var device = _outputDevices[offset + i];
            var index = offset + i;
            var isCurrent = device.Id == _outputCurrentDeviceId || (string.IsNullOrEmpty(device.Id) && string.IsNullOrEmpty(_outputCurrentDeviceId));
            var row = new HudButton
            {
                Height = 30,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 0, 2),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = (Brush)Application.Current.Resources[isCurrent ? "CursorBg" : "BgSoft"],
                BorderBrush = (Brush)Application.Current.Resources["Stroke"],
                BorderThickness = new Thickness(1),
            };
            var text = new Grid();
            text.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            text.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            text.Children.Add(new TextBlock
            {
                Text = device.Name,
                FontSize = 11.5,
                FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var mark = new TextBlock
            {
                Text = isCurrent ? "CURRENT" : device.IsDefault ? "DEFAULT" : "",
                FontSize = 8.5,
                Foreground = (Brush)Application.Current.Resources["TextDim"],
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 2, 0),
            };
            Grid.SetColumn(mark, 1);
            text.Children.Add(mark);
            row.Content = text;
            row.Click += (_, _) =>
            {
                _outputIndex = index;
                SelectOutput(device);
            };
            _outputButtons.Add(row);
            _outputRows.Children.Add(row);
        }

        var cursor = Math.Clamp(_outputIndex - offset, 0, Math.Max(0, _outputButtons.Count - 1));
        for (var i = 0; i < _outputButtons.Count; i++)
            _outputButtons[i].IsCursor = i == cursor;
    }

    private void SelectOutput(AudioDeviceInfo device)
    {
        if (!Services.Audio.RoutingSupported)
        {
            AppDetect.Open("ms-settings:apps-volume");
            _outputStatus!.Text = "Opened Windows per-app mixer";
            return;
        }

        if (string.IsNullOrEmpty(device.Id))
        {
            Services.Audio.ResetSessionOutput(_outputPid);
            Hud.Toast($"{_outputName}: system default");
            CloseOutputPicker();
            return;
        }

        var (ok, message) = Services.Audio.SetSessionOutput(_outputPid, device.Id);
        _outputStatus!.Text = ok ? "Routed to " + message : message;
        if (ok)
        {
            Hud.Toast($"{_outputName} → {message}");
            _outputCurrentDeviceId = device.Id;
            RenderOutputRows();
        }
    }

    private void MoveOutput(int delta)
    {
        if (_outputDevices.Count == 0) return;
        _outputIndex = Math.Clamp(_outputIndex + delta, 0, _outputDevices.Count - 1);
        RenderOutputRows();
    }

    public override bool Navigate(NavDir dir)
    {
        if (_outputOpen)
        {
            MoveOutput(dir is NavDir.Up or NavDir.Left ? -1 : 1);
            return true;
        }

        // On a volume row, left/right is a volume control; elsewhere it walks
        // the focus like the rest of the HUD.
        if (dir is NavDir.Left or NavDir.Right &&
            (Current == MasterButton || (Current is not null && _sessionButtons.Contains(Current))))
        {
            return Adjust(dir == NavDir.Right ? 1 : -1);
        }

        return base.Navigate(dir);
    }

    public override void Confirm()
    {
        if (_outputOpen)
        {
            if (_outputIndex >= 0 && _outputIndex < _outputDevices.Count)
                SelectOutput(_outputDevices[_outputIndex]);
            return;
        }
        base.Confirm();
    }

    public override bool Back()
    {
        if (_outputOpen)
        {
            CloseOutputPicker();
            return true;
        }
        return false;
    }

    public override bool Secondary()
    {
        if (_outputOpen)
        {
            CloseOutputPicker();
            return true;
        }

        if (Current == MasterButton)
        {
            AppDetect.Open("ms-settings:apps-volume");
            return true;
        }

        if (Current?.Tag is int sessionPid)
        {
            var s = Services.Audio.Current.Sessions.FirstOrDefault(x => x.Pid == sessionPid);
            if (s is not null)
            {
                OpenOutputPicker(s.Pid, s.Name, s.DeviceId);
                return true;
            }
        }
        return false;
    }

    public override bool Adjust(int direction)
    {
        var step = direction > 0 ? 1 : -1;
        if (Current == MasterButton)
        {
            Services.Audio.SetMasterVolume(Services.Audio.Current.Master + step * 0.03f);
            ForceRefresh();
            return true;
        }

        if (Current?.Tag is int sessionPid)
        {
            Services.Audio.AdjustSessionVolume(sessionPid, step * 0.05f);
            ForceRefresh();
            return true;
        }
        return false;
    }


}
