using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Media;
using SableHUD.Core;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using SysPlaybackStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace SableHUD.Media;

public enum MediaKind
{
    None,
    System,
    Local,
}

public sealed record MediaSnapshot(
    MediaKind Kind,
    string Source,
    string Title,
    string Artist,
    string Album,
    ImageSource? Artwork,
    TimeSpan Position,
    TimeSpan Duration,
    bool IsPlaying,
    bool CanNext,
    bool CanPrev,
    bool CanSeek,
    DateTimeOffset UpdatedAt)
{
    public bool HasTrack => Kind != MediaKind.None && !string.IsNullOrWhiteSpace(Title);
    public double Progress => Duration.TotalSeconds <= 0 ? 0 : Math.Clamp(Position.TotalSeconds / Duration.TotalSeconds, 0, 1);
    public string Key => $"{Kind}|{Source}|{Title}|{Artist}";

    public static readonly MediaSnapshot Empty = new(
        MediaKind.None, "", "", "", "", null, TimeSpan.Zero, TimeSpan.Zero, false, false, false, false, DateTimeOffset.MinValue);
}

/// <summary>
/// Reads whatever Windows is playing through SMTC. LiMusic publishes its state there
/// (media.rs in the LiMusic tree), which is what lets this HUD drive it without
/// linking the whole Tauri application.
/// </summary>
public sealed class SmtcService : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private ImageSource? _art;
    private string _artKey = "";
    private string _title = "";
    private string _artist = "";
    private string _durationKey = "";
    private TimeSpan _duration;

    public MediaSnapshot Current { get; private set; } = MediaSnapshot.Empty;
    public int SessionCount { get; private set; }
    public bool Ready => _manager is not null;
    public event Action? Updated;

    public void Start()
    {
        if (_loop is not null) return;
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                await PollAsync();
            }
            catch (Exception ex)
            {
                Log.Warn("smtc poll: " + ex.Message);
                _session = null;
                Current = MediaSnapshot.Empty;
                if (!ct.IsCancellationRequested) Updated?.Invoke();
            }

            try { await Task.Delay(500, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PollAsync()
    {
        var manager = _manager;
        if (manager is null) return;

        var sessions = manager.GetSessions();
        SessionCount = sessions.Count;
        if (sessions.Count == 0)
        {
            _session = null;
            if (Current.Kind != MediaKind.None)
            {
                Current = MediaSnapshot.Empty;
                Updated?.Invoke();
            }
            return;
        }

        // Keep the current session while it exists and is still the best choice.
        GlobalSystemMediaTransportControlsSession? chosen = null;
        if (_session is not null)
        {
            foreach (var s in sessions)
            {
                if (s.SourceAppUserModelId == _session.SourceAppUserModelId)
                {
                    chosen = s;
                    break;
                }
            }
        }

        chosen ??= sessions.FirstOrDefault(s => s.GetPlaybackInfo().PlaybackStatus == SysPlaybackStatus.Playing)
                  ?? sessions[0];

        _session = chosen;
        var info = chosen.GetPlaybackInfo();
        var timeline = chosen.GetTimelineProperties();
        var props = await chosen.TryGetMediaPropertiesAsync();

        var title = props?.Title ?? "";
        var artist = props?.Artist ?? "";
        var album = props?.AlbumTitle ?? "";
        var appId = chosen.SourceAppUserModelId ?? "";
        var source = FriendlySource(appId);
        var key = $"{title}|{artist}|{appId}";
        if (key != _artKey)
        {
            _artKey = key;
            _art = await LoadArtAsync(props?.Thumbnail);
        }

        var duration = timeline.EndTime;
        if (duration > TimeSpan.Zero)
        {
            _durationKey = key;
            _duration = duration;
        }
        else if (key == _durationKey)
        {
            // Some players drop EndTime while paused; keep the last known length.
            duration = _duration;
        }

        var position = timeline.Position;
        if (info.PlaybackStatus == SysPlaybackStatus.Playing)
        {
            var extra = DateTimeOffset.Now - timeline.LastUpdatedTime;
            if (extra > TimeSpan.Zero && extra < TimeSpan.FromMinutes(30)) position += extra;
        }
        if (duration > TimeSpan.Zero && position > duration) position = duration;

        var controls = info.Controls;
        Current = new MediaSnapshot(
            MediaKind.System,
            source,
            string.IsNullOrWhiteSpace(title) ? "Nothing playing" : title,
            artist,
            album,
            _art,
            position,
            duration,
            info.PlaybackStatus == SysPlaybackStatus.Playing,
            controls?.IsNextEnabled ?? false,
            controls?.IsPreviousEnabled ?? false,
            controls?.IsPlaybackPositionEnabled ?? true,
            DateTimeOffset.Now);

        _title = title;
        _artist = artist;
        Updated?.Invoke();
    }

    private static async Task<ImageSource?> LoadArtAsync(global::Windows.Storage.Streams.IRandomAccessStreamReference? reference)
    {
        if (reference is null) return null;
        try
        {
            using var stream = await reference.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > 12 * 1024 * 1024) return null;
            var bytes = new byte[stream.Size];
            using var reader = new global::Windows.Storage.Streams.DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.DecodePixelWidth = 480;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public async Task TogglePlayPauseAsync()
    {
        try
        {
            if (_session is not null) await _session.TryTogglePlayPauseAsync();
        }
        catch (Exception ex) { Log.Warn("smtc toggle: " + ex.Message); }
    }

    public async Task NextAsync()
    {
        try
        {
            if (_session is not null) await _session.TrySkipNextAsync();
        }
        catch (Exception ex) { Log.Warn("smtc next: " + ex.Message); }
    }

    public async Task PreviousAsync()
    {
        try
        {
            if (_session is not null) await _session.TrySkipPreviousAsync();
        }
        catch (Exception ex) { Log.Warn("smtc prev: " + ex.Message); }
    }

    public async Task SeekAsync(TimeSpan position)
    {
        try
        {
            if (_session is null) return;
            var timeline = _session.GetTimelineProperties();
            var max = timeline.EndTime > TimeSpan.Zero ? timeline.EndTime : position;
            var clamped = position < TimeSpan.Zero ? TimeSpan.Zero : (position > max ? max : position);
            await _session.TryChangePlaybackPositionAsync(clamped.Ticks);
        }
        catch (Exception ex) { Log.Warn("smtc seek: " + ex.Message); }
    }

    public static string FriendlySource(string aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return "Media";
        var lower = aumid.ToLowerInvariant();
        if (lower.Contains("limusic")) return "LiMusic";
        if (lower.Contains("spotify")) return "Spotify";
        if (lower.Contains("chrome")) return "Chrome";
        if (lower.Contains("msedge")) return "Edge";
        if (lower.Contains("firefox")) return "Firefox";
        if (lower.Contains("brave")) return "Brave";
        if (lower.Contains("opera")) return "Opera";
        if (lower.Contains("vlc")) return "VLC";
        if (lower.Contains("foobar")) return "foobar2000";
        if (lower.Contains("musicbee")) return "MusicBee";
        if (lower.Contains("zune") || lower.Contains("media player")) return "Media Player";
        if (lower.Contains("tidal")) return "TIDAL";
        if (lower.Contains("deezer")) return "Deezer";
        if (lower.Contains("amazon")) return "Amazon Music";
        if (lower.Contains("apple")) return "Apple Music";
        if (lower.Contains("youtube")) return "YouTube Music";

        var clean = Regex.Replace(aumid, @"[^A-Za-z0-9\.\-_ ]", " ");
        var last = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "Media";
        last = last.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
        return last.Length > 18 ? last[..18] : last;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(400); } catch { }
        _cts.Dispose();
    }
}
