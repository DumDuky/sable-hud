using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.Wave;
using SableHUD.Core;
using TagLib;

namespace SableHUD.Media;

public sealed record LocalTrack(
    string Path,
    string Title,
    string Artist,
    string Album,
    TimeSpan Duration)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

public enum RepeatMode
{
    Off,
    All,
    One,
}

/// <summary>Small local file player so the music widget still works with no media app running.</summary>
public sealed class LocalPlayer : IDisposable
{
    private readonly DispatcherTimer _ticker;
    private List<LocalTrack> _library = new();
    private int _index = -1;
    private WaveOutEvent? _output;
    private MediaFoundationReader? _reader;
    private bool _suppressStop;
    private CancellationTokenSource? _scanCts;
    private ImageSource? _art;

    public event Action? StateChanged;
    public event Action? LibraryChanged;

    public IReadOnlyList<LocalTrack> Library => _library;
    public LocalTrack? Current => _index >= 0 && _index < _library.Count ? _library[_index] : null;
    public int CurrentIndex => _index;
    public bool IsPlaying { get; private set; }
    public bool IsPaused { get; private set; }
    public RepeatMode Repeat { get; private set; } = RepeatMode.All;
    public bool Shuffle { get; private set; }
    public float Volume { get; set; } = 0.8f;
    public TimeSpan Position => SafePosition();
    public ImageSource? Artwork => _art;
    public bool IsScanning { get; private set; }
    public string ScanStatus { get; private set; } = "";

    public LocalPlayer()
    {
        _ticker = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _ticker.Tick += (_, _) =>
        {
            if (Current is not null) StateChanged?.Invoke();
        };
        _ticker.Start();
    }

    private TimeSpan SafePosition()
    {
        try { return _reader?.CurrentTime ?? TimeSpan.Zero; }
        catch { return TimeSpan.Zero; }
    }

    public async Task ScanAsync(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            _library = new List<LocalTrack>();
            _index = -1;
            LibraryChanged?.Invoke();
            return;
        }

        _scanCts?.Cancel();
        var cts = new CancellationTokenSource();
        _scanCts = cts;
        IsScanning = true;
        ScanStatus = "Scanning…";
        LibraryChanged?.Invoke();

        var found = await Task.Run(() => ScanFolder(folder, cts.Token), cts.Token).ConfigureAwait(true);
        if (cts.Token.IsCancellationRequested) return;

        _library = found;
        _index = _library.Count > 0 ? 0 : -1;
        IsScanning = false;
        ScanStatus = found.Count == 0 ? "No audio files found" : $"{found.Count} tracks";
        LibraryChanged?.Invoke();
    }

    private static List<LocalTrack> ScanFolder(string folder, CancellationToken ct)
    {
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".m4a", ".aac", ".flac", ".wav", ".wma",
        };
        var list = new List<LocalTrack>();
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories); }
        catch { return list; }

        foreach (var path in files)
        {
            if (ct.IsCancellationRequested) break;
            var ext = Path.GetExtension(path);
            if (!exts.Contains(ext)) continue;
            try
            {
                using var file = TagLib.File.Create(path);
                var title = string.IsNullOrWhiteSpace(file.Tag.Title)
                    ? Path.GetFileNameWithoutExtension(path)
                    : file.Tag.Title!;
                var artist = file.Tag.FirstPerformer ?? file.Tag.FirstAlbumArtist ?? "";
                var album = file.Tag.Album ?? "";
                var duration = file.Properties?.Duration ?? TimeSpan.Zero;
                list.Add(new LocalTrack(path, title, artist, album, duration));
            }
            catch
            {
                list.Add(new LocalTrack(path, Path.GetFileNameWithoutExtension(path), "", "", TimeSpan.Zero));
            }
        }

        return list.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    internal void LoadDemoTracks(IReadOnlyList<LocalTrack> tracks)
    {
        _library = tracks.ToList();
        _index = _library.Count > 0 ? 0 : -1;
        IsScanning = false;
        ScanStatus = $"{_library.Count} tracks";
        LibraryChanged?.Invoke();
    }

    public void PlayIndex(int index, bool fromUser = true)
    {
        if (_library.Count == 0) return;
        if (index < 0 || index >= _library.Count) return;
        _index = index;
        StartCurrent();
        if (fromUser) StateChanged?.Invoke();
    }

    public void PlayPause()
    {
        if (Current is null)
        {
            if (_library.Count > 0) PlayIndex(Math.Max(0, _index));
            return;
        }

        if (IsPlaying)
        {
            _output?.Pause();
            IsPlaying = false;
            IsPaused = true;
        }
        else if (_reader is not null && _output is not null)
        {
            _output.Play();
            IsPlaying = true;
            IsPaused = false;
        }
        else
        {
            StartCurrent();
        }
        StateChanged?.Invoke();
    }

    private void StartCurrent()
    {
        var track = Current;
        if (track is null) return;
        StopInternal();
        try
        {
            _reader = new MediaFoundationReader(track.Path);
            _output = new WaveOutEvent { Volume = Volume, DesiredLatency = 160 };
            _output.Init(_reader);
            _output.PlaybackStopped += OnPlaybackStopped;
            _output.Play();
            IsPlaying = true;
            IsPaused = false;
            _art = LoadArtwork(track);
        }
        catch (Exception ex)
        {
            Log.Warn("local play failed: " + ex.Message);
            IsPlaying = false;
            IsPaused = false;
        }
        StateChanged?.Invoke();
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (_suppressStop) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        dispatcher.BeginInvoke(() =>
        {
            if (_suppressStop) return;
            if (Repeat == RepeatMode.One)
            {
                StartCurrent();
                return;
            }
            if (_index < _library.Count - 1)
            {
                Next(auto: true);
            }
            else if (Repeat == RepeatMode.All && _library.Count > 0)
            {
                PlayIndex(0, fromUser: false);
            }
            else
            {
                StopInternal();
                IsPlaying = false;
                IsPaused = false;
                StateChanged?.Invoke();
            }
        });
    }

    private void StopInternal()
    {
        _suppressStop = true;
        try
        {
            if (_output is not null)
            {
                _output.PlaybackStopped -= OnPlaybackStopped;
                _output.Stop();
                _output.Dispose();
            }
        }
        catch { }
        _output = null;
        try { _reader?.Dispose(); } catch { }
        _reader = null;
        _suppressStop = false;
    }

    public void Next(bool auto = false)
    {
        if (_library.Count == 0) return;
        if (Shuffle && _library.Count > 1)
        {
            var rnd = Random.Shared.Next(_library.Count - 1);
            if (rnd >= _index) rnd++;
            PlayIndex(rnd, fromUser: false);
            return;
        }
        if (_index < _library.Count - 1) PlayIndex(_index + 1, fromUser: false);
        else if (Repeat == RepeatMode.All) PlayIndex(0, fromUser: false);
        else if (!auto) PlayIndex(0, fromUser: false);
        StateChanged?.Invoke();
    }

    public void Previous()
    {
        if (_library.Count == 0) return;
        if (SafePosition().TotalSeconds > 4 && _reader is not null)
        {
            _reader.CurrentTime = TimeSpan.Zero;
            StateChanged?.Invoke();
            return;
        }
        if (_index > 0) PlayIndex(_index - 1, fromUser: false);
        else if (Repeat == RepeatMode.All) PlayIndex(_library.Count - 1, fromUser: false);
        StateChanged?.Invoke();
    }

    public void Seek(TimeSpan position)
    {
        if (_reader is null) return;
        try
        {
            var total = _reader.TotalTime;
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            if (total > TimeSpan.Zero && position > total) position = total;
            _reader.CurrentTime = position;
            StateChanged?.Invoke();
        }
        catch { }
    }

    public void SeekRelative(double seconds)
    {
        if (_reader is null) return;
        Seek(SafePosition() + TimeSpan.FromSeconds(seconds));
    }

    public void SetVolume(float value)
    {
        Volume = Math.Clamp(value, 0f, 1f);
        if (_output is not null) _output.Volume = Volume;
        StateChanged?.Invoke();
    }

    public void ToggleShuffle()
    {
        Shuffle = !Shuffle;
        StateChanged?.Invoke();
    }

    public void CycleRepeat()
    {
        Repeat = Repeat switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off,
        };
        StateChanged?.Invoke();
    }

    private static ImageSource? LoadArtwork(LocalTrack track)
    {
        byte[]? bytes = null;
        try
        {
            using var file = TagLib.File.Create(track.Path);
            var pic = file.Tag.Pictures?.FirstOrDefault(p => p.Type == PictureType.FrontCover)
                      ?? file.Tag.Pictures?.FirstOrDefault();
            bytes = pic?.Data?.Data;
        }
        catch { }

        if (bytes is null)
        {
            var dir = Path.GetDirectoryName(track.Path);
            if (dir is not null)
            {
                foreach (var name in new[] { "cover.jpg", "folder.jpg", "front.jpg", "cover.png", "folder.png" })
                {
                    var candidate = Path.Combine(dir, name);
                    if (System.IO.File.Exists(candidate))
                    {
                        try { bytes = System.IO.File.ReadAllBytes(candidate); } catch { }
                        break;
                    }
                }
            }
        }

        if (bytes is null || bytes.Length == 0) return null;
        try
        {
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

    public void Dispose()
    {
        _ticker.Stop();
        _scanCts?.Cancel();
        StopInternal();
    }
}
