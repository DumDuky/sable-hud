using System.Collections.Concurrent;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SableHUD.Core;
using SableHUD.Shared;

namespace SableHUD.Media;

/// <summary>
/// Keeps the artwork of the playlist currently being played, so the HUD can show
/// either that cover or the song's own cover (Settings → Appearance → Artwork).
/// Tracks that arrived through radio/autoplay are not part of the playlist, so
/// they always keep their own art. Images are loaded once and frozen for use
/// from any thread.
/// </summary>
public sealed class CoverService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly ConcurrentDictionary<string, ImageSource> _cache = new();
    private readonly Dictionary<string, HashSet<string>> _artists = new();
    private string _url = "";
    private string _title = "";
    private ImageSource? _playlistArt;

    public event Action? Changed;

    public string PlaylistTitle => _title;
    public ImageSource? PlaylistArt => _playlistArt;

    public void SetPlaylist(string title, string? coverUrl, IEnumerable<LiSong>? songs = null)
    {
        _title = title ?? "";
        _artists.Clear();
        if (songs is not null)
        {
            foreach (var song in songs)
            {
                var key = Normalize(song.Title);
                if (key.Length == 0) continue;
                if (!_artists.TryGetValue(key, out var set))
                    _artists[key] = set = new HashSet<string>();
                set.Add(Normalize(song.Artists));
            }
        }

        var url = coverUrl ?? "";
        if (url == _url)
        {
            // Same art, but the queue (and therefore which tracks belong to the
            // playlist) changed: let the surfaces re-pick.
            Changed?.Invoke();
            return;
        }

        _url = url;
        _playlistArt = null;
        if (string.IsNullOrWhiteSpace(url))
        {
            Changed?.Invoke();
            return;
        }

        if (_cache.TryGetValue(url, out var cached))
        {
            _playlistArt = cached;
            Changed?.Invoke();
            return;
        }

        _ = LoadAsync(url);
    }

    public void ClearPlaylist()
    {
        _url = "";
        _title = "";
        _playlistArt = null;
        _artists.Clear();
        Changed?.Invoke();
    }

    /// <summary>Ask every surface to re-run <see cref="Pick"/>.</summary>
    public void NotifyChanged() => Changed?.Invoke();

    /// <summary>Which cover the HUD should draw right now.</summary>
    public ImageSource? Pick(MediaSnapshot snap)
    {
        if (Services.Settings.ArtworkSource.Equals("Playlist", StringComparison.OrdinalIgnoreCase) &&
            _playlistArt is not null &&
            snap.Kind == MediaKind.System &&
            snap.Source.Contains("LiMusic", StringComparison.OrdinalIgnoreCase) &&
            InPlaylist(snap))
            return _playlistArt;

        return snap.Artwork;
    }

    /// <summary>
    /// A radio track that followed the playlist is not in it: its own cover wins,
    /// even when the Playlist artwork setting is on.
    /// </summary>
    private bool InPlaylist(MediaSnapshot snap)
    {
        if (_artists.Count == 0) return true;

        var title = Normalize(snap.Title);
        if (title.Length == 0 || !_artists.TryGetValue(title, out var artists)) return false;

        var artist = Normalize(snap.Artist);
        if (artist.Length == 0 || artists.Count == 0) return true;

        foreach (var stored in artists)
        {
            if (stored.Length == 0) return true;
            if (stored == artist) return true;
            if (stored.Contains(artist, StringComparison.Ordinal) ||
                artist.Contains(stored, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var chars = value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray();
        return new string(chars);
    }

    private async Task LoadAsync(string url)
    {
        try
        {
            var bytes = await ReadAsync(url).ConfigureAwait(false);
            if (bytes.Length == 0 || bytes.Length > 16 * 1024 * 1024) return;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.DecodePixelWidth = 640;
            bmp.EndInit();
            bmp.Freeze();

            _cache[url] = bmp;
            if (url == _url)
            {
                _playlistArt = bmp;
                Changed?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Log.Warn("playlist cover: " + ex.Message);
        }
    }

    private static async Task<byte[]> ReadAsync(string url)
    {
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return await Http.GetByteArrayAsync(url).ConfigureAwait(false);

        // Custom LiMusic covers are local files; only the URL form goes over HTTP.
        return File.Exists(url)
            ? await File.ReadAllBytesAsync(url).ConfigureAwait(false)
            : Array.Empty<byte>();
    }
}
