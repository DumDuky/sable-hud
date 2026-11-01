using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SableHUD.Controls;
using SableHUD.Core;
using SableHUD.Input;
using SableHUD.Shared;

namespace SableHUD.Windows;

public partial class SettingsWindow : Window
{
    private sealed class Row
    {
        public required HudButton Button { get; init; }
        public required Action Activate { get; init; }
        public Action<int>? Adjust { get; init; }
    }

    private sealed record Section(string Id, string Title, string Icon);

    private static readonly Section[] Sections =
    {
        new("General", "General", "IconSettings"),
        new("Widgets", "Widgets", "IconGrid"),
        new("Appearance", "Appearance", "IconMonitor"),
        new("About", "About", "IconDisc"),
    };

    private static readonly (string Value, string Label)[] TriggerOptions =
    {
        ("Guide", "Xbox button"),
        ("GuideDouble", "Xbox button ×2"),
        ("GuideHold", "Hold Xbox 0.6s"),
        ("LbRb", "LB + RB"),
        ("Custom", "Custom"),
    };

    private static readonly (string Value, string Label)[] CustomOptions =
    {
        ("Guide", "Xbox button"),
        ("A", "A"),
        ("B", "B"),
        ("X", "X"),
        ("Y", "Y"),
        ("LeftBumper", "LB"),
        ("RightBumper", "RB"),
        ("Back+Start", "View + Menu"),
        ("LB+RB", "LB + RB"),
    };

    private readonly List<Row> _rows = new();
    private readonly Dictionary<string, HudButton> _nav = new();
    private int _section;
    private int _cursor;
    private bool _allowClose;

    public bool IsOpen => IsVisible;
    public event Action? Dismissed;

    public SettingsWindow()
    {
        InitializeComponent();
        VersionLabel.Text = "Sable " + (typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0");

        BuildNav();
        ShowSection(0);

        CloseSettingsButton.Click += (_, _) => Dismiss();
        HeaderBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        PreviewKeyDown += OnKey;
        ShowActivated = true;
    }

    private void BuildNav()
    {
        NavStrip.Children.Clear();
        _nav.Clear();

        foreach (var section in Sections)
        {
            var button = new HudButton
            {
                Height = 38,
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(12, 0, 12, 0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Icon = Ui.Geo(section.Icon),
                IconSize = 15,
                IconMargin = new Thickness(0, 0, 10, 0),
                Content = section.Title,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4),
            };
            var target = section;
            button.Click += (_, _) => ShowSection(Array.IndexOf(Sections, target));
            _nav[section.Id] = button;
            NavStrip.Children.Add(button);
        }
    }

    private void ShowSection(int index)
    {
        _section = Math.Clamp(index, 0, Sections.Length - 1);
        SectionTitleTop.Text = Sections[_section].Title;
        foreach (var (id, button) in _nav)
            button.IsActive = id == Sections[_section].Id;

        _cursor = 0;
        SettingsScroll.ScrollToTop();
        Rebuild();
    }

    public void Open(string? section = null)
    {
        if (!string.IsNullOrWhiteSpace(section))
        {
            var idx = Array.FindIndex(Sections, s => s.Id.Equals(section, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) ShowSection(idx);
        }
        if (!IsVisible) Show();
        Activate();
        _cursor = Math.Clamp(_cursor, 0, Math.Max(0, _rows.Count - 1));
        SetCursor(_cursor);
    }

    public void Dismiss()
    {
        if (!IsVisible) return;
        Hide();
        Dismissed?.Invoke();
    }

    public void AllowClose() => _allowClose = true;

    public void CycleSection(int delta)
    {
        ShowSection((_section + delta + Sections.Length) % Sections.Length);
        SetCursor(0);
    }

    public void Navigate(NavDir dir)
    {
        if (_rows.Count == 0) return;
        switch (dir)
        {
            case NavDir.Up:
                SetCursor((_cursor - 1 + _rows.Count) % _rows.Count);
                break;
            case NavDir.Down:
                SetCursor((_cursor + 1) % _rows.Count);
                break;
            case NavDir.Left:
                _rows[Math.Clamp(_cursor, 0, _rows.Count - 1)].Adjust?.Invoke(-1);
                break;
            case NavDir.Right:
                _rows[Math.Clamp(_cursor, 0, _rows.Count - 1)].Adjust?.Invoke(1);
                break;
        }
    }

    public void Confirm()
    {
        if (_cursor >= 0 && _cursor < _rows.Count) _rows[_cursor].Activate();
    }

    public void Secondary() => Dismiss();

    private void SetCursor(int index)
    {
        if (_rows.Count == 0) return;
        _cursor = Math.Clamp(index, 0, _rows.Count - 1);
        for (var i = 0; i < _rows.Count; i++) _rows[i].Button.IsCursor = i == _cursor;

        var row = _rows[_cursor].Button;
        Dispatcher.BeginInvoke(() => EnsureRowVisible(row), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void EnsureRowVisible(HudButton row)
    {
        if (!row.IsLoaded || SettingsScroll.ViewportHeight <= 0) return;
        try
        {
            var y = row.TransformToAncestor(SettingsScroll).Transform(new Point(0, 0)).Y;
            var bottom = y + row.ActualHeight;
            if (y < 8)
                SettingsScroll.ScrollToVerticalOffset(SettingsScroll.VerticalOffset + y - 8);
            else if (bottom > SettingsScroll.ViewportHeight - 8)
                SettingsScroll.ScrollToVerticalOffset(SettingsScroll.VerticalOffset + bottom - SettingsScroll.ViewportHeight + 8);
        }
        catch { }
    }

    private void Rebuild()
    {
        ContentHost.Children.Clear();
        _rows.Clear();

        switch (Sections[_section].Id)
        {
            case "General": BuildGeneral(); break;
            case "Widgets": BuildWidgets(); break;
            case "Appearance": BuildAppearance(); break;
            default: BuildAbout(); break;
        }

        SetCursor(Math.Clamp(_cursor, 0, Math.Max(0, _rows.Count - 1)));
    }

    private void AddHeader(string title, string subtitle)
    {
        ContentHost.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["Heading"],
            FontSize = 21,
        });
        ContentHost.Children.Add(new TextBlock
        {
            Text = subtitle,
            Style = (Style)Application.Current.Resources["Body"],
            FontSize = 11.5,
            Margin = new Thickness(0, 5, 0, 18),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 560,
        });
    }

    private Row AddRow(string label, string description, string value, Action activate, Action<int>? adjust = null, bool? toggle = null, bool chevron = true)
    {
        var button = new HudButton
        {
            Height = 62,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(15, 0, 13, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = (Brush)Application.Current.Resources["CardBg"],
            BorderBrush = (Brush)Application.Current.Resources["Stroke"],
            BorderThickness = new Thickness(1),
            Margin = new Thickness(9, 0, 9, 7),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        left.Children.Add(new TextBlock { Text = label, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        left.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 10.2,
            Foreground = (Brush)Application.Current.Resources["TextDim"],
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
            Margin = new Thickness(0, 2, 0, 0),
        });

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (toggle is bool on)
        {
            var pill = new Border
            {
                Width = 34,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = (Brush)Application.Current.Resources[on ? "Text" : "BgSoft"],
                BorderBrush = (Brush)Application.Current.Resources["StrokeStrong"],
                BorderThickness = new Thickness(1),
            };
            var dot = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = (Brush)Application.Current.Resources[on ? "OnAccent" : "TextDim"],
                HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            pill.Child = dot;
            right.Children.Add(pill);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                right.Children.Add(new TextBlock
                {
                    Text = value,
                    FontSize = 10.8,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.Resources["TextMuted"],
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxWidth = 190,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 5, 0),
                });
            }
            if (chevron)
            {
                right.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = Ui.Geo("IconChevronRight"),
                    Fill = (Brush)Application.Current.Resources["TextDim"],
                    Width = 12,
                    Height = 12,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 1, 0),
                });
            }
        }

        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        button.Content = grid;

        var row = new Row { Button = button, Activate = activate, Adjust = adjust };
        button.MouseEnter += (_, _) => SetCursor(_rows.IndexOf(row));
        button.Click += (_, _) =>
        {
            SetCursor(_rows.IndexOf(row));
            row.Activate();
        };

        _rows.Add(row);
        ContentHost.Children.Add(button);
        return row;
    }

    private void AddToggle(string label, string description, Func<bool> get, Action<bool> set)
    {
        AddRow(label, description, "", () =>
        {
            set(!get());
            Services.Save();
            Rebuild();
        }, _ =>
        {
            set(!get());
            Services.Save();
            Rebuild();
        }, toggle: get());
    }

    private void AddChoice<T>(string label, string description, IReadOnlyList<T> options, Func<T> get, Action<T> set,
        Func<T, string> display)
    {
        var current = get();
        var index = options.ToList().IndexOf(current);
        var next = options[(index + 1 + options.Count) % options.Count];
        AddRow(label, description, display(current), () =>
        {
            set(next);
            Services.Save();
            Rebuild();
        }, dir =>
        {
            var i = Math.Clamp(options.ToList().IndexOf(get()), 0, options.Count - 1);
            set(options[(i + dir + options.Count) % options.Count]);
            Services.Save();
            Rebuild();
        });
    }

    private void BuildGeneral()
    {
        AddHeader("General", "How Sable opens, how long it stays, and how big it is on your screen.");

        AddChoice("Xbox button gesture",
            "A single press is the default. If your pad's Guide button is captured by another app, try a double press or LB + RB.",
            TriggerOptions,
            () => TriggerOptions.FirstOrDefault(t => t.Value == Services.Settings.Trigger, TriggerOptions[0]),
            t => Services.Settings.Trigger = t.Value,
            t => t.Label);

        if (Services.Settings.Trigger == "Custom")
        {
            AddChoice("Custom gesture",
                "This is the exact button or chord Sable listens for. Navigation still uses the D-pad.",
                CustomOptions,
                () => CustomOptions.FirstOrDefault(t => t.Value == Services.Settings.CustomTrigger, CustomOptions[0]),
                t => Services.Settings.CustomTrigger = t.Value,
                t => t.Label);
        }

        AddRow("Windows Game Bar",
            "Windows uses the same Xbox press for Game Bar, so both open. Turn its controller shortcut off to keep this HUD alone.",
            "Turn off",
            () =>
            {
                var ok = AppDetect.DisableGameBarControllerButton();
                Dismiss();
                Hud.Toast(ok ? "Game Bar Xbox shortcut off — sign out if it persists" : "Turn off Game Bar's controller button");
                AppDetect.Open("ms-settings:gaming-gamebar");
            });

        AddChoice("Auto-hide",
            "Sable slides away after this much idle time. Any controller or mouse input resets the timer.",
            new[] { 0, 8, 12, 20, 30, 60 },
            () => Services.Settings.AutoHideSeconds,
            v => Services.Settings.AutoHideSeconds = v,
            v => v == 0 ? "Never" : $"{v} seconds");

        AddChoice("HUD size",
            "Comfortable is tuned for a 1440p monitor; Large is easier to read from a couch or TV.",
            new[] { "Compact", "Comfortable", "Large" },
            () => Services.Settings.Scale,
            v => Services.Settings.Scale = v,
            v => v);

        AddToggle("Keyboard shortcut",
            "Ctrl + Alt + H toggles the HUD. Ctrl + Alt + N toggles the mini player. Useful before a pad is connected.",
            () => Services.Settings.KeyboardHotkey,
            v => Services.Settings.KeyboardHotkey = v);

        AddToggle("Controller support",
            "Poll the pad for the trigger and navigation. Turn off to leave the pad entirely to the game.",
            () => Services.Settings.ControllerEnabled,
            v => Services.Settings.ControllerEnabled = v);

        AddToggle("Rumble on open and close",
            "A short, soft pulse confirms the HUD appeared when you are not looking at the screen.",
            () => Services.Settings.Rumble,
            v => Services.Settings.Rumble = v);

        AddToggle("Start with Windows",
            "Launch Sable quietly at sign-in. It stays invisible until you press the trigger.",
            () => Services.Settings.StartWithWindows,
            v =>
            {
                Services.Settings.StartWithWindows = v;
                Autostart.Set(v);
            });

        var installed = HidHide.IsInstalled;
        AddRow("Controller isolation driver",
            installed
                ? "HidHide is installed. Re-run this if you moved Sable or connected a new pad."
                : "Optional HidHide (MIT) driver. Hides the physical pad from the game only while the HUD is open. Requires a reboot after install.",
            installed ? "Reconfigure" : "Install",
            () =>
            {
                if (!installed)
                {
                    var opened = HidHide.Install();
                    Hud.Toast(opened ? "HidHide installer opened — reboot when it asks" : "HidHide installer not found in the package");
                    return;
                }

                _ = PrepareIsolationAsync();
            });

        AddRow("Isolate pad while HUD is open",
            "Hides the controller from games while the overlay is up. Apps opened after the HUD is up are isolated; an app that already opened the controller keeps it until it restarts or reloads. Sable is allowlisted.",
            Services.Settings.IsolateController ? "On" : "Off",
            () => _ = ToggleIsolationAsync(),
            toggle: Services.Settings.IsolateController);

        if (installed)
        {
            AddRow("Automatic pad reset",
                "Restarts the controller when the HUD opens, so apps that were already running lose the old handle. One Windows admin prompt to set up; after that it happens on every open.",
                HidHide.PadResetTaskExists() ? "On" : "Set up",
                SetUpPadReset);
        }
    }

    private void SetUpPadReset()
    {
        try
        {
            var exe = Paths.ExePath;
            if (string.IsNullOrWhiteSpace(exe) ||
                System.IO.Path.GetFileName(exe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                Hud.Toast("Run the packaged SableHUD.exe to set this up");
                return;
            }

            Process.Start(new ProcessStartInfo(exe, "--install-pad-reset")
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            Hud.Toast("Approve the Windows prompt, then reopen Settings");
        }
        catch
        {
            Hud.Toast("Pad reset setup was cancelled");
        }
    }

    private async Task ConnectLiMusicBridgeAsync()
    {
        Hud.Toast("Connecting LiMusic…");
        var ok = await Services.Media.LiMusic.EnsureAsync(true);
        Hud.Toast(ok ? "LiMusic HUD browser ready" : Services.Media.LiMusic.Status);
        Rebuild();
    }

    private async Task ToggleIsolationAsync()
    {
        if (!Services.Settings.IsolateController)
        {
            if (!HidHide.IsInstalled)
            {
                Hud.Toast("Install the controller isolation driver first");
                return;
            }

            var (ok, message) = await HidHide.PrepareAsync();
            if (!ok)
            {
                Hud.Toast(message);
                return;
            }

            Services.Settings.IsolateController = true;
            Services.Save();
            Hud.Toast(message + " · Reload apps that were already open");
            if (!HidHide.PadResetTaskExists()) SetUpPadReset();
        }
        else
        {
            Services.Settings.IsolateController = false;
            Services.Save();
            await HidHide.SetCloakAsync(false);
            Hud.Toast("Controller isolation off");
        }

        Rebuild();
    }

    private async Task PrepareIsolationAsync()
    {
        var (ok, message) = await HidHide.PrepareAsync();
        Hud.Toast(message);
        if (ok) await HidHide.SetCloakAsync(false);
    }

    private void BuildWidgets()
    {
        AddHeader("Widgets", "Pick what lives in the overlay, and where the music comes from.");

        AddToggle("Music", "Now playing, transport, artwork, and the in-HUD LiMusic browser.", () => Services.Settings.ShowMusic, v => SetWidget(v, () => Services.Settings.ShowMusic = v));

        AddRow("LiMusic HUD browser",
            "Search songs and playlists inside the widget using LiMusic's own signed-in backend. Enabling this restarts LiMusic once with a loopback HUD link.",
            Services.Media.LiMusic.State switch
            {
                SableHUD.Media.LiBridgeState.Connected => "Connected",
                SableHUD.Media.LiBridgeState.NeedsRestart => "Restart",
                SableHUD.Media.LiBridgeState.NotInstalled => "Not found",
                SableHUD.Media.LiBridgeState.Error => "Retry",
                _ => "Connect",
            },
            () => _ = ConnectLiMusicBridgeAsync());
        AddToggle("Performance", "CPU, GPU, memory, network, FPS, foreground app and session time.", () => Services.Settings.ShowPerformance, v => SetWidget(v, () => Services.Settings.ShowPerformance = v));
        AddToggle("Audio", "System volume, microphone mute and a per-app mixer.", () => Services.Settings.ShowAudio, v => SetWidget(v, () => Services.Settings.ShowAudio = v));
        AddToggle("Tools", "Screenshot, microphone, sound mixer, Game Bar, LiMusic link and controller setup.", () => Services.Settings.ShowTools, v => SetWidget(v, () => Services.Settings.ShowTools = v));

        AddToggle("Mini player",
            "A small floating now-playing strip. Y toggles it; drag it anywhere with the mouse. It hides while the HUD is open.",
            () => Services.Settings.ShowMiniPlayer,
            v => Services.Settings.ShowMiniPlayer = v);

        AddToggle("Autoplay / radio",
            "Let LiMusic continue with a radio queue after the playlist or album ends. Turn off to stop at the end of what you started.",
            () => Services.Settings.Autoplay,
            v => Services.Settings.Autoplay = v);

        AddToggle("Track-change toast",
            "When a new song starts and the HUD is hidden, a compact now-playing pebble slides in for a few seconds.",
            () => Services.Settings.ToastOnTrackChange,
            v => Services.Settings.ToastOnTrackChange = v);

        AddToggle("FPS readout",
            "Reads RivaTuner Statistics Server's shared frame data when RTSS is running. No injection, no anti-cheat surface.",
            () => Services.Settings.RtssFps,
            v => Services.Settings.RtssFps = v);

        var limusic = AppDetect.FindLimusic(Services.Settings.LimusicPath);
        AddRow("LiMusic path",
            limusic ?? "Not found automatically. Point Sable at limusic.exe so the Tools tile can open it.",
            limusic is null ? "Locate" : "Found",
            () =>
            {
                var chosen = Dialogs.PickExe(this, "Locate limusic.exe");
                if (chosen is null) return;
                Services.Settings.LimusicPath = chosen;
                Services.Save();
                Hud.Toast("LiMusic path saved");
                Rebuild();
            });

        AddChoice("Mini player position",
            "Where the floating strip lives. Custom is set automatically when you drag it with the mouse.",
            new[] { "TopLeft", "TopCenter", "TopRight", "MiddleLeft", "Center", "MiddleRight", "BottomLeft", "BottomCenter", "BottomRight", "Custom" },
            () => Services.Settings.MiniDock,
            v => Services.Settings.MiniDock = v,
            v => v switch
            {
                "TopLeft" => "Top left",
                "TopCenter" => "Top center",
                "TopRight" => "Top right",
                "MiddleLeft" => "Middle left",
                "Center" => "Center",
                "MiddleRight" => "Middle right",
                "BottomLeft" => "Bottom left",
                "BottomCenter" => "Bottom center",
                "BottomRight" => "Bottom right",
                _ => "Custom (drag)",
            });
    }

    private List<string> EnabledTabs()
    {
        var tabs = new List<string>();
        if (Services.Settings.ShowMusic) tabs.Add("Music");
        if (Services.Settings.ShowPerformance) tabs.Add("Performance");
        if (Services.Settings.ShowAudio) tabs.Add("Audio");
        if (Services.Settings.ShowTools) tabs.Add("Tools");
        if (tabs.Count == 0) tabs.Add("Music");
        return tabs;
    }

    private void SetWidget(bool value, Action apply)
    {
        apply();
        if (!Services.Settings.ShowMusic && !Services.Settings.ShowPerformance &&
            !Services.Settings.ShowAudio && !Services.Settings.ShowTools)
        {
            Services.Settings.ShowMusic = true;
            Hud.Toast("Keep at least one widget on");
        }

        var tabs = EnabledTabs();
        if (!tabs.Contains(Services.Settings.DefaultTab))
            Services.Settings.DefaultTab = tabs[0];

        Services.Save();
        Rebuild();
    }

    private void BuildAppearance()
    {
        AddHeader("Appearance", "Ink, paper and graphite. No neon, no purple — just four quiet monochrome themes.");

        AddChoice("Theme",
            "Applied to the HUD, the mini player, the toast and this window immediately.",
            Themes.All,
            () => Themes.Get(Services.Settings.Theme),
            t =>
            {
                Services.Settings.Theme = t.Id;
                ThemeManager.Apply(t.Id);
            },
            t => t.Name);

        AddChoice("Now-playing artwork",
            "Show each song's own cover, or the cover of the playlist you pressed play from.",
            new[] { "Song", "Playlist" },
            () => Services.Settings.ArtworkSource,
            v =>
            {
                Services.Settings.ArtworkSource = v;
                Services.Cover.NotifyChanged();
            },
            v => v == "Playlist" ? "Playlist cover" : "Song cover");

        AddChoice("Default widget",
            "Which page greets you when the HUD drops down.",
            EnabledTabs(),
            () => Services.Settings.DefaultTab,
            v => Services.Settings.DefaultTab = v,
            v => v);

        AddRow("Reset appearance", "Back to Noir, comfortable size, music first.", "Reset", () =>
        {
            Services.Settings.Theme = "Noir";
            Services.Settings.Scale = "Comfortable";
            Services.Settings.DefaultTab = "Music";
            ThemeManager.Apply("Noir");
            Services.Save();
            Hud.Toast("Appearance reset");
            Rebuild();
        });
    }

    private void BuildAbout()
    {
        AddHeader("Sable HUD", $"Version {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0"} · a quiet, controller-first overlay for Windows 11.");

        var paragraphs = new[]
        {
            "Music is built around LiMusic. LiMusic publishes its now-playing state to Windows (SMTC), so Sable can show artwork, seek, and play or skip without embedding the whole Tauri app. The music widget is a stripped-down, controller-sized take on that player; LiMusic itself still owns search, library and streaming.",
            "You can also point Sable at a folder of your own music. MP3, FLAC, M4A and WAV are read through Media Foundation and tagged with TagLib.",
            "FPS comes from RivaTuner Statistics Server's shared memory when RTSS is running. Sable never injects into a game.",
            "Windows broadcasts controller input to every app. HidHide can hide the pad while the HUD is open, but an app that already opened the controller keeps it until it restarts or reloads. Borderless fullscreen keeps the overlay visible.",
        };

        foreach (var text in paragraphs)
        {
            ContentHost.Children.Add(new TextBlock
            {
                Text = text,
                Style = (Style)Application.Current.Resources["Body"],
                FontSize = 11.2,
                LineHeight = 17,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 570,
                Margin = new Thickness(0, 0, 0, 13),
            });
        }

        AddRow("LiMusic on GitHub", "The GPL-3.0 YouTube Music client this music experience is built around.", "Open", () =>
            AppDetect.Open("https://github.com/SimoHypers/limusic"));

        AddRow("Data folder", "Settings, logs and the local cache live here.", "Open", () =>
            AppDetect.Open(Paths.DataDir));

        AddRow("Open log", "If something misbehaves, this is the file to read first.", "Open", () =>
        {
            if (File.Exists(Paths.LogFile)) AppDetect.Open(Paths.LogFile);
            else Hud.Toast("No log yet");
        });

        AddRow("Exit Sable", "Stop the HUD and the pad listener. Music keeps playing in its own app.", "Exit", () =>
            Application.Current.Shutdown());

        ContentHost.Children.Add(new TextBlock
        {
            Text = "Sable HUD is licensed GPL-3.0 to match the LiMusic work it integrates with. Themes in this app are a monochrome interpretation of LiMusic's swappable theme system.",
            Style = (Style)Application.Current.Resources["Dim"],
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 560,
            Margin = new Thickness(2, 6, 0, 0),
        });
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                Navigate(NavDir.Up);
                e.Handled = true;
                break;
            case Key.Down:
                Navigate(NavDir.Down);
                e.Handled = true;
                break;
            case Key.Left:
                Navigate(NavDir.Left);
                e.Handled = true;
                break;
            case Key.Right:
                Navigate(NavDir.Right);
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Space:
                Confirm();
                e.Handled = true;
                break;
            case Key.Escape:
                Dismiss();
                e.Handled = true;
                break;
            case Key.Tab:
                CycleSection(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
                e.Handled = true;
                break;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
