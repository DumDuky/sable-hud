using SableHUD.Core;
using System.Windows.Threading;

namespace SableHUD.Media;

/// <summary>
/// Owns the two music sources and decides which one the HUD is showing.
/// Local playback and an external session never play at the same time: whichever
/// starts last politely pauses the other.
/// </summary>
public sealed class MediaCoordinator : IDisposable
{
    private readonly DispatcherTimer _timer;
    private MediaKind _active = MediaKind.None;
    private bool _systemWasPlaying;
    private bool _suppressMutualPause;
    private string _lastLocalKey = "";
    private string _optimisticKey = "";
    private bool _optimisticPlaying;
    private DateTime _optimisticUntil = DateTime.MinValue;

    public SmtcService System { get; } = new();
    public LiMusicBridge LiMusic { get; } = new();
    public LocalPlayer Local { get; }

    public MediaSnapshot Current { get; private set; } = MediaSnapshot.Empty;
    public string BridgeRepeat { get; private set; } = "off";
    public bool BridgeShuffle { get; private set; }

    public event Action<MediaSnapshot>? Changed;

    public MediaCoordinator(LocalPlayer local, float localVolume)
    {
        Local = local;
        Local.Volume = localVolume;
        Local.StateChanged += OnLocalStateChanged;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        System.Start();
    }

    private void OnLocalStateChanged()
    {
        var track = Local.Current;
        var key = $"{track?.Path}|{Local.IsPlaying}|{Local.IsPaused}";
        if (key == _lastLocalKey) return;
        _lastLocalKey = key;

        if (track is not null && (Local.IsPlaying || Local.IsPaused))
            _active = MediaKind.Local;

        if (Local.IsPlaying && !_suppressMutualPause && System.Current.IsPlaying)
        {
            _suppressMutualPause = true;
            _ = System.TogglePlayPauseAsync().ContinueWith(_ => _suppressMutualPause = false);
        }

        Tick();
    }

    private void Tick()
    {
        var sys = System.Current;
        var local = Local.Current;

        if (sys.IsPlaying && !_systemWasPlaying)
        {
            _active = MediaKind.System;
            if (Local.IsPlaying && !_suppressMutualPause)
            {
                _suppressMutualPause = true;
                Local.PlayPause();
                _suppressMutualPause = false;
            }
        }
        _systemWasPlaying = sys.IsPlaying;

        var pick = _active switch
        {
            MediaKind.Local when local is not null => LocalSnapshot(local),
            MediaKind.System when sys.HasTrack => sys,
            _ => PickDefault(sys, local),
        };

        if (DateTime.UtcNow < _optimisticUntil && pick.HasTrack && pick.Key == _optimisticKey)
        {
            pick = pick with { IsPlaying = _optimisticPlaying };
        }
        else if (DateTime.UtcNow >= _optimisticUntil)
        {
            _optimisticKey = "";
        }

        Current = pick;
        Changed?.Invoke(pick);
    }

    private MediaSnapshot PickDefault(MediaSnapshot sys, LocalTrack? local)
    {
        if (local is not null && (Local.IsPlaying || Local.IsPaused))
        {
            _active = MediaKind.Local;
            return LocalSnapshot(local);
        }
        if (sys.HasTrack)
        {
            _active = MediaKind.System;
            return sys;
        }
        if (local is not null && Local.IsPlaying)
        {
            _active = MediaKind.Local;
            return LocalSnapshot(local);
        }
        _active = MediaKind.None;
        return MediaSnapshot.Empty;
    }

    private MediaSnapshot LocalSnapshot(LocalTrack track) => new(
        MediaKind.Local,
        "Local library",
        track.Title,
        track.Artist,
        track.Album,
        Local.Artwork,
        Local.IsPlaying || Local.IsPaused ? Local.Position : TimeSpan.Zero,
        track.Duration,
        Local.IsPlaying,
        true,
        true,
        true,
        DateTimeOffset.Now);

    public void TogglePlayPause()
    {
        if (Current.Kind == MediaKind.Local)
        {
            Local.PlayPause();
            return;
        }

        if (Current.HasTrack)
        {
            _optimisticKey = Current.Key;
            _optimisticPlaying = !Current.IsPlaying;
            _optimisticUntil = DateTime.UtcNow.AddMilliseconds(1400);
            Current = Current with { IsPlaying = _optimisticPlaying, UpdatedAt = DateTimeOffset.Now };
            Changed?.Invoke(Current);
        }

        if (LiMusic.IsConnected) CallBridge(LiMusic.TogglePauseAsync, System.TogglePlayPauseAsync);
        else if (Current.Kind == MediaKind.System) _ = System.TogglePlayPauseAsync();
    }

    public void Next()
    {
        if (Current.Kind == MediaKind.Local) Local.Next();
        else if (LiMusic.IsConnected) CallBridge(LiMusic.NextAsync, System.NextAsync);
        else if (Current.Kind == MediaKind.System) _ = System.NextAsync();
    }

    public void Previous()
    {
        if (Current.Kind == MediaKind.Local) Local.Previous();
        else if (LiMusic.IsConnected) CallBridge(LiMusic.PreviousAsync, System.PreviousAsync);
        else if (Current.Kind == MediaKind.System) _ = System.PreviousAsync();
    }

    public void SeekRelative(double seconds)
    {
        if (Current.Kind == MediaKind.Local)
        {
            Local.SeekRelative(seconds);
            return;
        }

        var target = Current.Position + TimeSpan.FromSeconds(seconds);
        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        if (Current.Duration > TimeSpan.Zero && target > Current.Duration) target = Current.Duration;

        if (LiMusic.IsConnected)
            CallBridge(() => LiMusic.SeekAsync(target.TotalSeconds), () => System.SeekAsync(target));
        else if (Current.Kind == MediaKind.System)
            _ = System.SeekAsync(target);
    }

    public void SeekTo(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        if (Current.Kind == MediaKind.Local)
        {
            Local.Seek(TimeSpan.FromSeconds(Current.Duration.TotalSeconds * fraction));
            return;
        }

        if (Current.Duration <= TimeSpan.Zero) return;
        var target = TimeSpan.FromSeconds(Current.Duration.TotalSeconds * fraction);
        if (LiMusic.IsConnected)
            CallBridge(() => LiMusic.SeekAsync(target.TotalSeconds), () => System.SeekAsync(target));
        else if (Current.Kind == MediaKind.System)
            _ = System.SeekAsync(target);
    }

    public void ToggleShuffle()
    {
        if (Current.Kind == MediaKind.Local)
        {
            Local.ToggleShuffle();
            return;
        }

        BridgeShuffle = !BridgeShuffle;
        if (LiMusic.IsConnected) CallBridge(LiMusic.ToggleShuffleAsync, () => Task.CompletedTask);
    }

    public void CycleRepeat()
    {
        if (Current.Kind == MediaKind.Local)
        {
            Local.CycleRepeat();
            return;
        }

        if (!LiMusic.IsConnected) return;
        BridgeRepeat = BridgeRepeat switch
        {
            "off" => "all",
            "all" => "one",
            _ => "off",
        };
        CallBridge(() => LiMusic.SetRepeatAsync(BridgeRepeat), () => Task.CompletedTask);
    }

    private static void CallBridge(Func<Task> bridge, Func<Task> fallback)
    {
        _ = Task.Run(async () =>
        {
            try { await bridge().ConfigureAwait(false); }
            catch (Exception ex)
            {
                Log.Warn("limusic bridge command: " + ex.Message);
                try { await fallback().ConfigureAwait(false); } catch { }
            }
        });
    }

    public void SetLocalVolume(float v) => Local.SetVolume(v);

    public void Dispose()
    {
        _timer.Stop();
        Local.StateChanged -= OnLocalStateChanged;
        LiMusic.Dispose();
        System.Dispose();
        Local.Dispose();
    }
}
