using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SableHUD.Controls;
using SableHUD.Core;
using SableHUD.Input;
using SableHUD.Media;
using SableHUD.Shared;

namespace SableHUD.Widgets;

public partial class MusicView : HudPageBase
{
    private readonly List<HudButton> _rowButtons = new();
    private readonly List<Ellipse> _rowDots = new();
    private readonly HudButton[] _actions;
    private bool _libraryMode;
    private bool _actionsFocus;
    private int _actionIndex;
    private int _libIndex;
    private int _libOffset;
    private ImageSource? _lastArt;

    private enum BrowserMode
    {
        Closed,
        Library,
        Playlist,
        Results,
        Keyboard,
        Gate,
    }

    private Grid? _browserRoot;
    private TextBlock? _browserTitle;
    private TextBlock? _browserStatus;
    private TextBlock? _browserQuery;
    private StackPanel? _browserRows;
    private Grid? _keyboardPanel;
    private readonly List<HudButton> _browserButtons = new();
    private readonly List<List<HudButton>> _keyRows = new();
    private BrowserMode _browserMode = BrowserMode.Closed;
    private BrowserMode _keyboardReturn = BrowserMode.Closed;
    private BrowserMode _resultsReturn = BrowserMode.Closed;
    private int _browserIndex;
    private int _browserCount;
    private int _keyRow;
    private int _keyCol;
    private bool _browserBusy;
    private string _searchText = "";
    private string _playlistTitle = "";
    private string _playlistId = "";
    private string _playlistCoverUrl = "";
    private string? _playlistContinuation;
    private string _browserNote = "";
    private List<LiSong> _songs = new();
    private List<LiBrowseItem> _browseItems = new();

    public override string TabId => "Music";
    public override bool KeepVisible => _browserOpen;

    public MusicView()
    {
        InitializeComponent();
        _actions = new[] { FolderButton, RescanButton, BackButton };

        PlayButton.Click += (_, _) => Services.Media.TogglePlayPause();
        PrevButton.Click += (_, _) => Services.Media.Previous();
        NextButton.Click += (_, _) => Services.Media.Next();
        ShuffleButton.Click += (_, _) =>
        {
            Services.Media.ToggleShuffle();
            var localShuffle = Services.Media.Current.Kind == MediaKind.Local;
            var shuffleOn = localShuffle ? Services.Media.Local.Shuffle : Services.Media.BridgeShuffle;
            Hud.Toast(shuffleOn ? "Shuffle on" : "Shuffle off");
        };
        RepeatButton.Click += (_, _) =>
        {
            Services.Media.CycleRepeat();
            var localRepeat = Services.Media.Current.Kind == MediaKind.Local;
            var mode = localRepeat ? Services.Media.Local.Repeat.ToString().ToLowerInvariant() : Services.Media.BridgeRepeat;
            Hud.Toast($"Repeat: {mode}");
        };
        OpenMusicButton.Click += (_, _) => OpenLibraryBrowser();
        LibraryButton.Click += (_, _) => OpenSearchKeyboard();
        SeekHit.MouseLeftButtonDown += OnSeekClicked;
        BuildBrowser();

        FolderButton.Click += (_, _) => ChooseFolder();
        RescanButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(Services.Settings.MusicFolder)) ChooseFolder();
            else _ = Services.Media.Local.ScanAsync(Services.Settings.MusicFolder);
        };
        BackButton.Click += (_, _) => ExitLibrary();

        Services.Media.Local.LibraryChanged += OnLibraryChanged;
        RegisterNow();
    }

    private void RegisterNow()
    {
        ClearNav();
        Register(PlayButton);
        Register(PrevButton);
        Register(NextButton);
        Register(ShuffleButton);
        Register(RepeatButton);
        Register(OpenMusicButton);
        Register(LibraryButton);
        SetCursorIndex(0);
    }

    private void OnLibraryChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(OnLibraryChanged);
            return;
        }
        LibraryCount.Text = Services.Media.Local.Library.Count == 1
            ? "1 track"
            : $"{Services.Media.Local.Library.Count} tracks";
        if (_libraryMode) RenderLibrary();
    }

    public override void Activate()
    {
        Refresh();
        if (_browserOpen)
        {
            if (_browserMode == BrowserMode.Keyboard) SetKeyCursor();
            else if (_browserButtons.Count > 0) RenderCurrentRows();
            return;
        }
        if (_libraryMode)
        {
            if (_actionsFocus) FocusAction(_actionIndex);
            else FocusRow();
        }
        else if (!Services.Demo && !Services.Media.Current.HasTrack)
        {
            SetCursor(OpenMusicButton);
        }
        else
        {
            base.Activate();
        }
    }

    public override void Deactivate() { }

    public override void Refresh()
    {
        if (_browserOpen)
        {
            if (!_browserBusy && _browserMode != BrowserMode.Gate && !Services.Media.LiMusic.IsConnected)
                SetBrowserStatus("LiMusic HUD link closed");
            return;
        }

        var snap = Services.Demo ? Services.DemoMedia ?? MediaSnapshot.Empty : Services.Media.Current;
        var has = snap.HasTrack;

        SourceText.Text = (snap.Kind == MediaKind.None ? "MEDIA" : snap.Source).ToUpperInvariant();
        SourceDot.Opacity = snap.IsPlaying ? 1 : 0.3;
        StateText.Text = snap.Kind == MediaKind.None ? "Idle" : snap.IsPlaying ? "Playing" : "Paused";

        TrackTitle.Text = has ? snap.Title : "Silence";
        TrackArtist.Text = has
            ? (string.IsNullOrWhiteSpace(snap.Artist) ? "Unknown artist" : snap.Artist)
            : "Start something in LiMusic, or open your local library.";
        TrackAlbum.Text = has ? snap.Album : "";

        SeekBar.Value = has ? snap.Progress : 0;
        ElapsedText.Text = has ? Ui.Time(snap.Position) : "0:00";
        DurationText.Text = has && snap.Duration > TimeSpan.Zero ? Ui.Time(snap.Duration) : "--:--";

        PlayButton.Icon = Ui.Geo(snap.IsPlaying ? "IconPause" : "IconPlay");
        PlayButton.IsEnabled = has;
        PrevButton.IsEnabled = has;
        NextButton.IsEnabled = has;
        ShuffleButton.IsEnabled = has;
        RepeatButton.IsEnabled = has;
        var localSource = snap.Kind == MediaKind.Local;
        ShuffleButton.IsActive = localSource ? Services.Media.Local.Shuffle : Services.Media.BridgeShuffle;
        RepeatButton.IsActive = localSource ? Services.Media.Local.Repeat != RepeatMode.Off : Services.Media.BridgeRepeat != "off";

        SideSource.Text = snap.Kind == MediaKind.None ? "Nothing yet" : snap.Source;
        if (snap.Kind == MediaKind.Local)
        {
            SideLabel.Text = "NEXT UP";
            var lib = Services.Media.Local.Library;
            var next = lib.Count > 1 ? lib[(Services.Media.Local.CurrentIndex + 1) % lib.Count] : null;
            SideDetail.Text = next is null ? "The library is quiet." : $"{next.Title}\n{next.Artist}";
        }
        else if (snap.Kind == MediaKind.System)
        {
            SideLabel.Text = "ALBUM";
            SideDetail.Text = string.IsNullOrWhiteSpace(snap.Album)
                ? $"{snap.Source} is playing outside the overlay."
                : snap.Album;
        }
        else
        {
            SideLabel.Text = "GET STARTED";
            SideDetail.Text = "Play something in LiMusic, or point the HUD at a folder of your own music.";
        }

        UpdateArt(snap);

        if (_libraryMode) UpdateRowStates();
    }

    private void UpdateArt(MediaSnapshot snap)
    {
        var art = Services.Demo ? snap.Artwork : Services.Cover.Pick(snap);
        if (art is null && Services.Demo && snap.HasTrack) art = Ui.DemoArt();
        if (ReferenceEquals(art, _lastArt)) return;
        _lastArt = art;
        ArtBrush.ImageSource = art;
        ArtFallback.Visibility = art is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSeekClicked(object sender, MouseButtonEventArgs e)
    {
        if (!Services.Media.Current.HasTrack) return;
        var x = e.GetPosition(SeekHit).X;
        var w = SeekHit.ActualWidth;
        if (w <= 0) return;
        Services.Media.SeekTo(x / w);
        e.Handled = true;
    }

    // ---------------------------------------------------------------------
    // LiMusic browser
    // ---------------------------------------------------------------------

    private void BuildBrowser()
    {
        if (_browserRoot is not null || Content is not Grid root) return;

        var browser = new Grid
        {
            Visibility = Visibility.Collapsed,
            Background = Brushes.Transparent,
        };
        browser.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        browser.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        browser.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        browser.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        browser.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var back = new HudButton
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Padding = new Thickness(0),
            Icon = Ui.Geo("IconBack"), IconSize = 13, IconMargin = new Thickness(0),
            ToolTip = "Back (B)",
        };
        back.Click += (_, _) => BrowserBack();

        _browserTitle = new TextBlock
        {
            Text = "LiMusic",
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _browserStatus = new TextBlock
        {
            Text = "",
            FontSize = 10.5,
            Foreground = (Brush)Application.Current.Resources["TextDim"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 360,
            Margin = new Thickness(14, 0, 2, 0),
        };

        var searchButton = new HudButton
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Padding = new Thickness(0),
            Icon = Ui.Geo("IconSearch"), IconSize = 13, IconMargin = new Thickness(0),
            ToolTip = "Search (X)",
        };
        searchButton.Click += (_, _) => OpenSearchKeyboard();

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(searchButton);
        right.Children.Add(_browserStatus);

        Grid.SetColumn(back, 0);
        Grid.SetColumn(_browserTitle, 2);
        Grid.SetColumn(right, 3);
        header.Children.Add(back);
        header.Children.Add(_browserTitle);
        header.Children.Add(right);
        browser.Children.Add(header);

        var content = new Grid();
        _browserRows = new StackPanel();
        content.Children.Add(_browserRows);

        _keyboardPanel = new Grid { Visibility = Visibility.Collapsed };
        _keyboardPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _keyboardPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _keyboardPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _browserQuery = new TextBlock
        {
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 0, 0, 6),
            Text = "Search LiMusic",
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _keyboardPanel.Children.Add(_browserQuery);

        var keys = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        for (var c = 0; c < 10; c++)
            keys.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var r = 0; r < 3; r++)
            keys.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        Grid.SetRow(keys, 1);
        _keyboardPanel.Children.Add(keys);

        var layouts = new[]
        {
            "QWERTYUIOP",
            "ASDFGHJKL",
            "ZXCVBNM.,?",
        };
        for (var r = 0; r < layouts.Length; r++)
        {
            var rowButtons = new List<HudButton>();
            for (var c = 0; c < layouts[r].Length; c++)
            {
                var ch = layouts[r][c];
                var key = MakeKey(ch.ToString(), () => TypeCharacter(ch));
                Grid.SetRow(key, r);
                Grid.SetColumn(key, c);
                keys.Children.Add(key);
                rowButtons.Add(key);
            }
            _keyRows.Add(rowButtons);
        }

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 0),
        };
        var space = MakeKey("Space", () => TypeCharacter(' '), 180);
        var backspace = MakeKey("⌫", () => Backspace(), 64);
        var clear = MakeKey("Clear", () => { _searchText = ""; UpdateQuery(); }, 70);
        var search = MakeKey("Search", () => _ = RunSearchAsync(), 100);
        search.Background = (Brush)Application.Current.Resources["Accent"];
        search.Foreground = (Brush)Application.Current.Resources["OnAccent"];
        search.Solid = true;
        actions.Children.Add(space);
        actions.Children.Add(backspace);
        actions.Children.Add(clear);
        actions.Children.Add(search);
        Grid.SetRow(actions, 2);
        _keyboardPanel.Children.Add(actions);
        _keyRows.Add(new List<HudButton> { space, backspace, clear, search });

        content.Children.Add(_keyboardPanel);
        Grid.SetRow(content, 2);
        browser.Children.Add(content);

        var hint = new TextBlock
        {
            Text = "B back · X search · LT/RT page",
            FontSize = 9.5,
            Foreground = (Brush)Application.Current.Resources["TextDim"],
            Margin = new Thickness(2, 0, 0, 0),
        };
        Grid.SetRow(hint, 4);
        browser.Children.Add(hint);

        browser.Visibility = Visibility.Collapsed;
        root.Children.Add(browser);
        _browserRoot = browser;
    }

    private HudButton MakeKey(string text, Action action, double width = double.NaN)
    {
        var key = new HudButton
        {
            Height = 26,
            Width = width,
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(0),
            FontSize = 10.5,
            Content = text,
            Margin = new Thickness(2, 1, 2, 1),
            Background = (Brush)Application.Current.Resources["BgSoft"],
            BorderBrush = (Brush)Application.Current.Resources["Stroke"],
            BorderThickness = new Thickness(1),
        };
        key.Click += (_, _) => action();
        return key;
    }

    private void TypeCharacter(char ch)
    {
        if (_searchText.Length >= 48) return;
        _searchText += ch;
        UpdateQuery();
    }

    private void Backspace()
    {
        if (_searchText.Length == 0) return;
        _searchText = _searchText[..^1];
        UpdateQuery();
    }

    private void UpdateQuery()
    {
        if (_browserQuery is null) return;
        _browserQuery.Text = string.IsNullOrEmpty(_searchText) ? "Type a song, artist or album" : _searchText + "▍";
        _browserQuery.Foreground = (Brush)Application.Current.Resources[string.IsNullOrEmpty(_searchText) ? "TextDim" : "Text"];
    }

    private void OpenLibraryBrowser()
    {
        ShowBrowser();
        _browserMode = BrowserMode.Library;
        _ = LoadLibraryAsync();
    }

    private void OpenSearchKeyboard()
    {
        if (!_browserOpen)
        {
            ShowBrowser();
            _keyboardReturn = BrowserMode.Closed;
            _resultsReturn = BrowserMode.Closed;
            _browserMode = BrowserMode.Keyboard;
            BuildBrowserKeyboard();
            return;
        }

        if (_browserMode == BrowserMode.Keyboard) return;

        if (_browserMode == BrowserMode.Results)
        {
            // Editing a search: leaving the keyboard exits the search flow
            // instead of bouncing back into the results.
            _keyboardReturn = _resultsReturn;
        }
        else
        {
            // A new search started from the library/playlist or now-playing.
            _resultsReturn = _browserMode == BrowserMode.Playlist ? BrowserMode.Library : _browserMode;
            _keyboardReturn = _resultsReturn;
        }

        _browserMode = BrowserMode.Keyboard;
        BuildBrowserKeyboard();
    }

    private bool _browserOpen => _browserRoot?.Visibility == Visibility.Visible;

    private void ShowBrowser()
    {
        if (_browserRoot is null) return;
        NowPanel.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Collapsed;
        _browserRoot.Visibility = Visibility.Visible;
        ClearNav();
    }

    private void CloseBrowser()
    {
        if (_browserRoot is null) return;
        _browserRoot.Visibility = Visibility.Collapsed;
        _browserMode = BrowserMode.Closed;
        _keyboardReturn = BrowserMode.Closed;
        _resultsReturn = BrowserMode.Closed;
        _browserRows?.Children.Clear();
        _browserButtons.Clear();
        NowPanel.Visibility = Visibility.Visible;
        RegisterNow();
        Refresh();
    }

    private void BuildBrowserKeyboard()
    {
        if (_keyboardPanel is null) return;
        _browserRows!.Visibility = Visibility.Collapsed;
        _keyboardPanel.Visibility = Visibility.Visible;
        _browserTitle!.Text = "Search LiMusic";
        _browserStatus!.Text = "";
        _browserStatus.Text = string.IsNullOrEmpty(_browserNote) ? "Type, then Search" : _browserNote;
        UpdateQuery();
        _keyRow = Math.Clamp(_keyRow, 0, _keyRows.Count - 1);
        _keyCol = 0;
        SetKeyCursor();
    }

    private void SetKeyCursor()
    {
        for (var r = 0; r < _keyRows.Count; r++)
        {
            for (var c = 0; c < _keyRows[r].Count; c++)
                _keyRows[r][c].IsCursor = r == _keyRow && c == _keyCol;
        }
    }

    private void NavigateKeyboard(NavDir dir)
    {
        if (_keyRows.Count == 0) return;
        switch (dir)
        {
            case NavDir.Left:
                _keyCol = Math.Max(0, _keyCol - 1);
                break;
            case NavDir.Right:
                _keyCol = Math.Min(_keyRows[_keyRow].Count - 1, _keyCol + 1);
                break;
            case NavDir.Up:
                _keyRow = Math.Max(0, _keyRow - 1);
                _keyCol = Math.Min(_keyCol, _keyRows[_keyRow].Count - 1);
                break;
            case NavDir.Down:
                _keyRow = Math.Min(_keyRows.Count - 1, _keyRow + 1);
                _keyCol = Math.Min(_keyCol, _keyRows[_keyRow].Count - 1);
                break;
        }
        SetKeyCursor();
    }

    private void BrowserBack()
    {
        switch (_browserMode)
        {
            case BrowserMode.Keyboard:
                _keyboardPanel!.Visibility = Visibility.Collapsed;
                _browserRows!.Visibility = Visibility.Visible;
                if (_keyboardReturn is BrowserMode.Playlist or BrowserMode.Library)
                {
                    _browserMode = _keyboardReturn;
                    if (_browserMode == BrowserMode.Playlist && _songs.Count == 0)
                        _browserMode = BrowserMode.Library;
                    if (_browserMode == BrowserMode.Library && _browseItems.Count == 0)
                    {
                        _ = LoadLibraryAsync();
                        break;
                    }
                    RenderCurrentRows();
                }
                else
                {
                    CloseBrowser();
                }
                break;
            case BrowserMode.Results:
                // Back edits the search, as the hint promises. Leaving the
                // keyboard then exits to the library/now-playing, so the two
                // screens no longer bounce into each other forever.
                OpenSearchKeyboard();
                break;
            case BrowserMode.Playlist:
                if (_browseItems.Count > 0)
                {
                    _browserMode = BrowserMode.Library;
                    _browserIndex = 0;
                    RenderCurrentRows();
                }
                else
                {
                    _ = LoadLibraryAsync();
                }
                break;
            default:
                CloseBrowser();
                break;
        }
    }

    private async Task LoadLibraryAsync()
    {
        if (_browserBusy) return;
        SetBusy("Connecting to LiMusic…");
        try
        {
            if (!await Services.Media.LiMusic.EnsureAsync(false))
            {
                ShowGate();
                return;
            }

            _browseItems = (await Services.Media.LiMusic.GetLibraryAsync())
                .Where(x => x.Kind.Equals("playlist", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (!_browseItems.Any(x => x.Id == "VLLM" || x.Title.Equals("Liked Music", StringComparison.OrdinalIgnoreCase)))
                _browseItems.Insert(0, new LiBrowseItem("playlist", "VLLM", "Liked Music", "Your likes", default));
            _browserMode = BrowserMode.Library;
            _browserIndex = 0;
            _browserNote = "A opens a playlist · X searches · LT/RT pages";
            RenderCurrentRows();
            SetBrowserStatus($"{_browseItems.Count} playlists");
        }
        catch (Exception ex)
        {
            ShowGate(ex.Message);
        }
        finally
        {
            _browserBusy = false;
        }
    }

    private void ShowGate(string? error = null)
    {
        _browserMode = BrowserMode.Gate;
        _browserIndex = 0;
        _browserCount = 1;
        _browserTitle!.Text = "LiMusic";
        _browserStatus!.Text = error ?? Services.Media.LiMusic.Status;
        _browserRows!.Visibility = Visibility.Visible;
        _keyboardPanel!.Visibility = Visibility.Collapsed;
        _browserRows.Children.Clear();
        _browserButtons.Clear();

        var row = NewBrowserRow("Connect LiMusic", "Starts LiMusic with the local HUD link", () => _ = ConnectAsync(), true);
        _browserButtons.Add(row);
        _browserRows.Children.Add(row);
        _browserNote = "A connects · B closes";
        SetBrowserCursor(0);
    }

    private async Task ConnectAsync()
    {
        if (_browserBusy) return;
        SetBusy("Starting LiMusic…");
        var ok = false;
        try
        {
            ok = await Services.Media.LiMusic.EnsureAsync(true);
            if (!ok) ShowGate();
        }
        catch (Exception ex)
        {
            ShowGate(ex.Message);
        }
        finally
        {
            _browserBusy = false;
        }

        if (ok) await LoadLibraryAsync();
    }

    private async Task RunSearchAsync()
    {
        var query = _searchText.Trim();
        if (query.Length == 0 || _browserBusy) return;
        SetBusy("Searching LiMusic…");
        try
        {
            if (!await Services.Media.LiMusic.EnsureAsync(false))
            {
                ShowGate();
                return;
            }

            _songs = (await Services.Media.LiMusic.SearchAsync(query)).ToList();
            _browserMode = BrowserMode.Results;
            _browserIndex = 0;
            _browserNote = "A plays · B edits the search · LT/RT pages";
            RenderCurrentRows();
            SetBrowserStatus($"{_songs.Count} results");
        }
        catch (Exception ex)
        {
            ShowGate(ex.Message);
        }
        finally
        {
            _browserBusy = false;
        }
    }

    private async Task OpenPlaylistAsync(LiBrowseItem item)
    {
        if (_browserBusy) return;
        SetBusy($"Opening {item.Title}…");
        try
        {
            if (!await Services.Media.LiMusic.EnsureAsync(false))
            {
                ShowGate();
                return;
            }

            var playlist = await Services.Media.LiMusic.GetPlaylistAsync(item.Id);
            _songs = playlist.Songs.ToList();
            _playlistTitle = string.IsNullOrWhiteSpace(playlist.Title) ? item.Title : playlist.Title;
            _playlistId = item.Id;
            _playlistCoverUrl = playlist.CoverUrl;
            _playlistContinuation = playlist.Continuation;
            _browserMode = BrowserMode.Playlist;
            _browserIndex = 0;
            _browserNote = "A plays · B back to library · LT/RT pages";
            RenderCurrentRows();
            SetBrowserStatus($"{_songs.Count} tracks");
        }
        catch (Exception ex)
        {
            ShowGate(ex.Message);
        }
        finally
        {
            _browserBusy = false;
        }
    }

    private async Task PlaySongAsync(LiSong song, int index)
    {
        try
        {
            SetBrowserStatus("Starting…");
            if (!await Services.Media.LiMusic.EnsureAsync(false))
            {
                ShowGate();
                return;
            }

            if (_browserMode == BrowserMode.Playlist && _songs.Count > 0)
            {
                Services.Cover.SetPlaylist(_playlistTitle, _playlistCoverUrl, _songs);
                await Services.Media.LiMusic.PlayPlaylistAsync(
                    _songs,
                    Math.Clamp(index, 0, _songs.Count - 1),
                    _playlistId,
                    _playlistTitle,
                    _playlistContinuation);
            }
            else
            {
                Services.Cover.ClearPlaylist();
                await Services.Media.LiMusic.PlayAsync(song.Raw);
            }

            SetBrowserStatus($"Playing · {song.Title}");
            Hud.Toast("Playing " + song.Title);
        }
        catch (Exception ex)
        {
            SetBrowserStatus(ex.Message);
        }
    }

    private void SetBusy(string text)
    {
        _browserBusy = true;
        SetBrowserStatus(text);
    }

    private void SetBrowserStatus(string text)
    {
        if (_browserStatus is not null)
            _browserStatus.Text = text;
    }

    private void RenderCurrentRows()
    {
        if (_browserRows is null || _keyboardPanel is null) return;
        _browserRows.Visibility = Visibility.Visible;
        _keyboardPanel.Visibility = Visibility.Collapsed;
        _browserRows.Children.Clear();
        _browserButtons.Clear();

        if (_browserMode == BrowserMode.Library)
        {
            _browserCount = _browseItems.Count;
            _browserTitle!.Text = "Your LiMusic library";
            if (_browseItems.Count == 0)
            {
                ShowGate("No playlists returned");
                return;
            }

            const int visible = 5;
            _browserIndex = Math.Clamp(_browserIndex, 0, _browseItems.Count - 1);
            var offset = Math.Clamp(_browserIndex - 2, 0, Math.Max(0, _browseItems.Count - visible));
            for (var i = 0; i < visible && offset + i < _browseItems.Count; i++)
            {
                var item = _browseItems[offset + i];
                var index = offset + i;
                var row = NewBrowserRow(item.Title, item.Subtitle, () =>
                {
                    _browserIndex = index;
                    _ = OpenPlaylistAsync(item);
                });
                _browserButtons.Add(row);
                _browserRows.Children.Add(row);
            }
            SetBrowserCursor(_browserIndex - offset);
        }
        else if (_browserMode == BrowserMode.Playlist || _browserMode == BrowserMode.Results)
        {
            _browserCount = _songs.Count;
            _browserTitle!.Text = _browserMode == BrowserMode.Playlist
                ? _playlistTitle
                : string.IsNullOrEmpty(_searchText) ? "Search results" : $"Results for “{_searchText}”";
            if (_songs.Count == 0)
            {
                ShowGate("Nothing found");
                return;
            }

            const int visible = 5;
            _browserIndex = Math.Clamp(_browserIndex, 0, _songs.Count - 1);
            var offset = Math.Clamp(_browserIndex - 2, 0, Math.Max(0, _songs.Count - visible));
            for (var i = 0; i < visible && offset + i < _songs.Count; i++)
            {
                var song = _songs[offset + i];
                var index = offset + i;
                var meta = string.IsNullOrWhiteSpace(song.Artists) ? song.Duration : $"{song.Artists}  ·  {song.Duration}";
                var row = NewBrowserRow(song.Title, meta, () =>
                {
                    _browserIndex = index;
                    _ = PlaySongAsync(song, index);
                });
                _browserButtons.Add(row);
                _browserRows.Children.Add(row);
            }
            SetBrowserCursor(_browserIndex - offset);
        }
    }

    private HudButton NewBrowserRow(string title, string meta, Action action, bool emphasis = false)
    {
        var button = new HudButton
        {
            Height = 31,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 0, 0, 2),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = (Brush)Application.Current.Resources[emphasis ? "CursorBg" : "BgSoft"],
            BorderBrush = (Brush)Application.Current.Resources["Stroke"],
            BorderThickness = new Thickness(1),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock
        {
            Text = title,
            FontSize = 11.8,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var detail = new TextBlock
        {
            Text = meta,
            FontSize = 9.8,
            Foreground = (Brush)Application.Current.Resources["TextDim"],
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 420,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(12, 0, 4, 0),
        };
        Grid.SetColumn(detail, 1);
        grid.Children.Add(name);
        grid.Children.Add(detail);
        button.Content = grid;
        button.Click += (_, _) =>
        {
            SetBrowserCursor(_browserButtons.IndexOf(button));
            action();
        };
        return button;
    }

    private void SetBrowserCursor(int index)
    {
        if (_browserButtons.Count == 0) return;
        index = Math.Clamp(index, 0, _browserButtons.Count - 1);
        for (var i = 0; i < _browserButtons.Count; i++)
            _browserButtons[i].IsCursor = i == index;
    }

    private void MoveBrowser(int delta)
    {
        if (_browserCount == 0) return;
        _browserIndex = Math.Clamp(_browserIndex + delta, 0, _browserCount - 1);
        RenderCurrentRows();
    }

    private void PageBrowser(int pages)
    {
        if (_browserCount == 0) return;
        _browserIndex = Math.Clamp(_browserIndex + pages * 5, 0, _browserCount - 1);
        RenderCurrentRows();
    }

    private void OpenLimusic()
    {
        if (AppDetect.OpenLimusic(Services.Settings.LimusicPath))
            Hud.Toast("LiMusic opened");
        else
            Hud.Toast("LiMusic isn't installed — set its path in Settings");
    }

    public override bool Navigate(NavDir dir)
    {
        if (_browserOpen)
        {
            if (_browserMode == BrowserMode.Keyboard)
            {
                NavigateKeyboard(dir);
                return true;
            }

            MoveBrowser(dir is NavDir.Up or NavDir.Left ? -1 : 1);
            return true;
        }

        if (_libraryMode) return NavigateLibrary(dir);
        return base.Navigate(dir);
    }

    public override void Confirm()
    {
        if (_browserOpen)
        {
            if (_browserMode == BrowserMode.Keyboard)
            {
                if (_keyRows.Count > 0 && _keyRow < _keyRows.Count && _keyCol < _keyRows[_keyRow].Count)
                    _keyRows[_keyRow][_keyCol].Invoke();
            }
            else
            {
                _browserButtons.FirstOrDefault(x => x.IsCursor)?.Invoke();
            }
            return;
        }

        if (!_libraryMode)
        {
            base.Confirm();
            return;
        }

        if (_actionsFocus)
        {
            _actions[Math.Clamp(_actionIndex, 0, _actions.Length - 1)].Invoke();
            return;
        }

        var lib = Services.Media.Local.Library;
        if (lib.Count > 0)
            Services.Media.Local.PlayIndex(Math.Clamp(_libIndex, 0, lib.Count - 1));
    }

    public override bool Adjust(int direction)
    {
        if (_browserOpen)
        {
            PageBrowser(direction > 0 ? 1 : -1);
            return true;
        }

        Services.Media.SeekRelative(direction > 0 ? 10 : -10);
        return true;
    }

    public override bool Back()
    {
        if (_browserOpen)
        {
            BrowserBack();
            return true;
        }
        if (_libraryMode)
        {
            ExitLibrary();
            return true;
        }
        return false;
    }

    public override bool Secondary()
    {
        if (_browserOpen)
        {
            OpenSearchKeyboard();
            return true;
        }
        if (_libraryMode)
        {
            ExitLibrary();
            return true;
        }
        Services.Media.ToggleShuffle();
        var local = Services.Media.Current.Kind == MediaKind.Local;
        var on = local ? Services.Media.Local.Shuffle : Services.Media.BridgeShuffle;
        Hud.Toast(on ? "Shuffle on" : "Shuffle off");
        return true;
    }

    internal void HideLibraryForRender() => ExitLibrary();

    internal void ShowLibraryForRender()
    {
        _libraryMode = true;
        NowPanel.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Visible;
        ClearNav();
        _libIndex = 0;
        _actionsFocus = false;
        RenderLibrary();
    }

    private void EnterLibrary()
    {
        if (string.IsNullOrWhiteSpace(Services.Settings.MusicFolder))
        {
            ChooseFolder();
            return;
        }

        _libraryMode = true;
        NowPanel.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Visible;
        ClearNav();
        _libIndex = Math.Max(0, Services.Media.Local.CurrentIndex);
        _libOffset = 0;
        _actionsFocus = false;
        _actionIndex = 0;
        if (Services.Media.Local.Library.Count == 0)
            _ = Services.Media.Local.ScanAsync(Services.Settings.MusicFolder);
        RenderLibrary();
    }

    private void ExitLibrary()
    {
        _libraryMode = false;
        LibraryPanel.Visibility = Visibility.Collapsed;
        NowPanel.Visibility = Visibility.Visible;
        RegisterNow();
        Refresh();
    }

    private void ChooseFolder()
    {
        var chosen = Dialogs.PickFolder(Window.GetWindow(this), Services.Settings.MusicFolder,
            "Choose a folder of music for the Sable HUD");
        if (chosen is null) return;

        Services.Settings.MusicFolder = chosen;
        Services.Save();
        _ = Services.Media.Local.ScanAsync(chosen);

        if (!_libraryMode)
        {
            _libraryMode = true;
            NowPanel.Visibility = Visibility.Collapsed;
            LibraryPanel.Visibility = Visibility.Visible;
            ClearNav();
        }
        RenderLibrary();
    }

    private void RenderLibrary()
    {
        LibraryRows.Children.Clear();
        _rowButtons.Clear();
        _rowDots.Clear();

        var lib = Services.Media.Local.Library;
        LibraryCount.Text = lib.Count == 1 ? "1 track" : $"{lib.Count} tracks";
        LibraryStatus.Text = Services.Media.Local.IsScanning ? "Scanning…" : "";

        if (lib.Count == 0)
        {
            LibraryRows.Children.Add(new TextBlock
            {
                Text = "No tracks yet. Choose a folder and Sable will scan it for MP3, FLAC, M4A and WAV files.",
                Style = (Style)Application.Current.Resources["Body"],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 8, 2, 0),
                MaxWidth = 520,
            });
            FocusAction(0);
            return;
        }

        const int visible = 5;
        _libIndex = Math.Clamp(_libIndex, 0, lib.Count - 1);
        if (_libIndex < _libOffset) _libOffset = _libIndex;
        if (_libIndex >= _libOffset + visible) _libOffset = _libIndex - visible + 1;
        _libOffset = Math.Clamp(_libOffset, 0, Math.Max(0, lib.Count - visible));

        for (var i = 0; i < visible && _libOffset + i < lib.Count; i++)
        {
            var index = _libOffset + i;
            var track = lib[index];
            var button = BuildRow(track, index);
            _rowButtons.Add(button);
            LibraryRows.Children.Add(button);
        }

        for (var i = 0; i < _actions.Length; i++)
            _actions[i].IsCursor = _actionsFocus && i == _actionIndex;

        UpdateRowStates();
        if (!_actionsFocus) FocusRow();
    }

    private HudButton BuildRow(LocalTrack track, int index)
    {
        var button = new HudButton
        {
            Height = 32,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(9, 0, 9, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 3),
            Background = (Brush)Application.Current.Resources["BgSoft"],
            BorderBrush = (Brush)Application.Current.Resources["Stroke"],
            BorderThickness = new Thickness(1),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Ellipse
        {
            Width = 5,
            Height = 5,
            Fill = (Brush)Application.Current.Resources["Text"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Opacity = 0,
        };
        var title = new TextBlock
        {
            Text = track.Title,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var artist = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(track.Artist) ? track.Album : track.Artist,
            FontSize = 9.6,
            Foreground = (Brush)Application.Current.Resources["TextDim"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            MaxWidth = 240,
        };
        var duration = new TextBlock
        {
            Text = Ui.Time(track.Duration),
            FontSize = 9.8,
            Foreground = (Brush)Application.Current.Resources["TextDim"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 2, 0),
        };
        Grid.SetColumn(dot, 0);
        Grid.SetColumn(title, 1);
        Grid.SetColumn(artist, 2);
        Grid.SetColumn(duration, 3);

        grid.Children.Add(dot);
        grid.Children.Add(title);
        grid.Children.Add(artist);
        grid.Children.Add(duration);
        button.Content = grid;

        button.Click += (_, _) =>
        {
            _libIndex = index;
            _actionsFocus = false;
            Services.Media.Local.PlayIndex(index);
            RenderLibrary();
        };

        _rowDots.Add(dot);
        return button;
    }

    private void UpdateRowStates()
    {
        if (!_libraryMode) return;
        var current = Services.Media.Local.CurrentIndex;
        for (var i = 0; i < _rowButtons.Count; i++)
        {
            var global = _libOffset + i;
            _rowDots[i].Opacity = global == current ? 1 : 0;
            _rowButtons[i].IsActive = false;
            _rowButtons[i].IsCursor = !_actionsFocus && global == _libIndex;
        }
    }

    private void FocusRow()
    {
        _actionsFocus = false;
        foreach (var a in _actions) a.IsCursor = false;
        UpdateRowStates();
    }

    private void FocusAction(int index)
    {
        _actionsFocus = true;
        _actionIndex = Math.Clamp(index, 0, _actions.Length - 1);
        foreach (var row in _rowButtons) row.IsCursor = false;
        for (var i = 0; i < _actions.Length; i++) _actions[i].IsCursor = i == _actionIndex;
    }

    private bool NavigateLibrary(NavDir dir)
    {
        var lib = Services.Media.Local.Library;
        if (_libraryMode && lib.Count == 0)
        {
            NavigateActions(dir);
            return true;
        }

        if (!_actionsFocus)
        {
            switch (dir)
            {
                case NavDir.Up:
                    if (_libIndex > 0)
                    {
                        _libIndex--;
                        RenderLibrary();
                    }
                    else
                    {
                        FocusAction(2);
                    }
                    return true;
                case NavDir.Down:
                    if (_libIndex < lib.Count - 1)
                    {
                        _libIndex++;
                        RenderLibrary();
                    }
                    else
                    {
                        FocusAction(0);
                    }
                    return true;
                case NavDir.Left:
                case NavDir.Right:
                    return true;
            }
        }
        else
        {
            switch (dir)
            {
                case NavDir.Left:
                    FocusAction((_actionIndex - 1 + _actions.Length) % _actions.Length);
                    return true;
                case NavDir.Right:
                    FocusAction((_actionIndex + 1) % _actions.Length);
                    return true;
                case NavDir.Up:
                    FocusRow();
                    return true;
                case NavDir.Down:
                    FocusAction((_actionIndex + 1) % _actions.Length);
                    return true;
            }
        }
        return false;
    }

    private void NavigateActions(NavDir dir)
    {
        switch (dir)
        {
            case NavDir.Left:
                FocusAction((_actionIndex - 1 + _actions.Length) % _actions.Length);
                break;
            case NavDir.Right:
            case NavDir.Down:
                FocusAction((_actionIndex + 1) % _actions.Length);
                break;
            case NavDir.Up:
                FocusAction((_actionIndex - 1 + _actions.Length) % _actions.Length);
                break;
        }
    }
}
