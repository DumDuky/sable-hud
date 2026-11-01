using SableHUD.Core;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace SableHUD.Media;

public enum LiBridgeState
{
    Disconnected,
    Connecting,
    Connected,
    NeedsRestart,
    NotInstalled,
    Error,
}

public sealed record LiSong(
    string VideoId,
    string Title,
    string Artists,
    string Album,
    string Duration,
    JsonElement Raw)
{
    public string Meta => string.IsNullOrWhiteSpace(Artists) ? Album : Artists;
}

public sealed record LiBrowseItem(
    string Kind,
    string Id,
    string Title,
    string Subtitle,
    JsonElement Raw);

public sealed record LiPlaylist(
    string Title,
    string CoverUrl,
    string? Continuation,
    IReadOnlyList<LiSong> Songs);

/// <summary>
/// A local bridge into the running LiMusic UI. LiMusic is a Tauri app; its webview
/// exposes the same typed commands its own interface uses. Enabling WebView2's
/// loopback DevTools port lets Sable call those commands directly, so the HUD can
/// search, browse playlists and start playback without rebuilding or reparenting
/// the whole Tauri application.
/// </summary>
public sealed class LiMusicBridge : IDisposable
{
    public const int DebugPort = 9222;
    private const string DebugArgument = "--remote-debugging-port=9222";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement?>> _pending = new();
    private readonly object _stateGate = new();
    private ClientWebSocket? _socket;
    private int _id;
    private LiBridgeState _state = LiBridgeState.Disconnected;
    private string _status = "LiMusic link idle";

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int command);

    public LiBridgeState State
    {
        get { lock (_stateGate) return _state; }
    }

    public string Status
    {
        get { lock (_stateGate) return _status; }
    }

    public bool IsConnected => _socket?.State == WebSocketState.Open;
    public event Action? Changed;

    private void Set(LiBridgeState state, string text)
    {
        lock (_stateGate)
        {
            _state = state;
            _status = text;
        }
        Changed?.Invoke();
    }

    public async Task<bool> TryConnectAsync()
    {
        if (IsConnected) return true;
        Set(LiBridgeState.Connecting, "Connecting to LiMusic…");
        try
        {
            var target = await FindTargetAsync().ConfigureAwait(false);
            if (target is null)
            {
                Set(LiBridgeState.Disconnected, "LiMusic HUD link is not open");
                return false;
            }

            var socket = new ClientWebSocket();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            await socket.ConnectAsync(new Uri(target), cts.Token).ConfigureAwait(false);
            _socket?.Dispose();
            _socket = socket;
            _ = Task.Run(ReceiveLoopAsync);
            Set(LiBridgeState.Connected, "LiMusic connected");
            return true;
        }
        catch (Exception ex)
        {
            _socket = null;
            Set(LiBridgeState.Disconnected, "LiMusic HUD link is not open");
            Log.Warn("limusic bridge connect: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Starts/restarts LiMusic with the loopback debug port. This is deliberately
    /// explicit: restarting is the only way to attach to an instance that was
    /// launched without the bridge.
    /// </summary>
    public async Task<bool> EnsureAsync(bool allowRestart)
    {
        if (await TryConnectAsync().ConfigureAwait(false)) return true;

        var path = AppDetect.FindLimusic(null);
        if (path is null)
        {
            Set(LiBridgeState.NotInstalled, "LiMusic not found");
            return false;
        }

        var running = Process.GetProcessesByName("limusic-app").Concat(Process.GetProcessesByName("limusic")).Any();
        if (!allowRestart)
        {
            Set(LiBridgeState.NeedsRestart,
                running ? "Restart LiMusic once to enable the HUD browser" : "Start LiMusic for the HUD browser");
            return false;
        }

        try
        {
            foreach (var process in Process.GetProcessesByName("limusic-app").Concat(Process.GetProcessesByName("limusic")))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                try { process.Dispose(); } catch { }
            }

            await Task.Delay(500).ConfigureAwait(false);
            Set(LiBridgeState.Connecting, "Starting LiMusic…");

            var info = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory,
            };
            info.Environment["WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"] = DebugArgument;
            var launched = Process.Start(info);

            for (var i = 0; i < 30; i++)
            {
                await Task.Delay(500).ConfigureAwait(false);
                if (await TryConnectAsync().ConfigureAwait(false))
                {
                    if (launched is not null)
                    {
                        await Task.Delay(1000).ConfigureAwait(false);
                        try
                        {
                            launched.Refresh();
                            if (launched.MainWindowHandle != IntPtr.Zero)
                                ShowWindow(launched.MainWindowHandle, 6); // minimize, keep it alive
                        }
                        catch { }
                    }
                    return true;
                }
            }

            Set(LiBridgeState.Error, "LiMusic started but the HUD link did not answer");
            return false;
        }
        catch (Exception ex)
        {
            Log.Warn("limusic bridge launch: " + ex.Message);
            Set(LiBridgeState.Error, "Could not start LiMusic");
            return false;
        }
    }

    private static async Task<string?> FindTargetAsync()
    {
        try
        {
            var json = await Http.GetStringAsync($"http://127.0.0.1:{DebugPort}/json").ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            string? fallback = null;
            foreach (var page in doc.RootElement.EnumerateArray())
            {
                if (!page.TryGetProperty("type", out var type) || type.GetString() != "page") continue;
                if (!page.TryGetProperty("webSocketDebuggerUrl", out var ws)) continue;
                if (!page.TryGetProperty("url", out var url)) continue;
                var u = url.GetString() ?? "";

                // LiMusic also runs a hidden BotGuard harness webview. It matches
                // "limusic" in its hostname but is not allowed to invoke commands.
                if (u.Contains("limusicharness", StringComparison.OrdinalIgnoreCase)) continue;
                if (u.Contains("tauri.localhost", StringComparison.OrdinalIgnoreCase))
                    return ws.GetString();
                if (u.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase))
                    fallback ??= ws.GetString();
            }

            return fallback;
        }
        catch { }
        return null;
    }

    private async Task ReceiveLoopAsync()
    {
        var socket = _socket;
        if (socket is null) return;
        var buffer = new byte[64 * 1024];
        var text = new StringBuilder();

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) break;
                text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage) continue;

                var message = text.ToString();
                text.Clear();
                HandleMessage(message);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("limusic bridge receive: " + ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_socket, socket))
                Set(LiBridgeState.Disconnected, "LiMusic HUD link closed");
            FailPending();
        }
    }

    private void HandleMessage(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            if (!doc.RootElement.TryGetProperty("id", out var idElement)) return;
            var id = idElement.GetInt32();
            if (!_pending.TryRemove(id, out var waiter)) return;

            if (!doc.RootElement.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("result", out var remote))
            {
                waiter.TrySetResult(null);
                return;
            }

            if (remote.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String)
            {
                using var inner = JsonDocument.Parse(value.GetString() ?? "null");
                var root = inner.RootElement.Clone();
                if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
                {
                    waiter.TrySetResult(root.TryGetProperty("data", out var data) ? data.Clone() : null);
                }
                else
                {
                    var err = root.TryGetProperty("error", out var e) ? e.GetString() : "LiMusic command failed";
                    waiter.TrySetException(new InvalidOperationException(err ?? "LiMusic command failed"));
                }
            }
            else
            {
                waiter.TrySetResult(null);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("limusic bridge message: " + ex.Message);
        }
    }

    private void FailPending()
    {
        foreach (var (_, waiter) in _pending)
            waiter.TrySetException(new InvalidOperationException("LiMusic link closed"));
        _pending.Clear();
    }

    public async Task<JsonElement?> InvokeAsync(string command, object? args = null)
    {
        if (!IsConnected && !await TryConnectAsync().ConfigureAwait(false))
            throw new InvalidOperationException(Status);

        var socket = _socket ?? throw new InvalidOperationException("LiMusic link is closed");
        JsonElement? response = null;
        var waiter = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = Interlocked.Increment(ref _id);
        _pending[id] = waiter;

        var argsJson = args is null ? "null" : JsonSerializer.Serialize(args);
        var cmdJson = JsonSerializer.Serialize(command);
        var expression =
            "(async()=>{try{" +
            $"const r=await window.__TAURI_INTERNALS__.invoke({cmdJson}, {argsJson});" +
            "return JSON.stringify({ok:true,data:r===undefined?null:r});" +
            "}catch(e){return JSON.stringify({ok:false,error:String((e&&e.message)||e)});}})()";

        var payload = JsonSerializer.Serialize(new
        {
            id,
            method = "Runtime.evaluate",
            @params = new
            {
                expression,
                awaitPromise = true,
                returnByValue = true,
                userGesture = true,
            },
        });

        await _sendGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }

        var completed = await Task.WhenAny(waiter.Task, Task.Delay(TimeSpan.FromSeconds(20))).ConfigureAwait(false);
        if (completed != waiter.Task)
        {
            _pending.TryRemove(id, out _);
            throw new TimeoutException("LiMusic did not answer in time");
        }
        response = await waiter.Task.ConfigureAwait(false);
        return response;
    }

    public async Task<IReadOnlyList<LiSong>> SearchAsync(string query)
    {
        var data = await InvokeAsync("search", new { query, recordHistory = false }).ConfigureAwait(false);
        return ParseSongs(data);
    }

    public async Task<IReadOnlyList<LiBrowseItem>> GetLibraryAsync()
    {
        var data = await InvokeAsync("get_library").ConfigureAwait(false);
        var list = new List<LiBrowseItem>();
        if (data is { ValueKind: JsonValueKind.Array } array)
        {
            foreach (var item in array.EnumerateArray())
            {
                var kind = Str(item, "kind");
                var id = Str(item, "id");
                var title = Str(item, "title");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title)) continue;
                list.Add(new LiBrowseItem(kind, id, title, Str(item, "subtitle"), item.Clone()));
            }
        }
        return list;
    }

    public async Task<LiPlaylist> GetPlaylistAsync(string id)
    {
        var data = await InvokeAsync("get_playlist", new { id, sort = (string?)null, desc = false }).ConfigureAwait(false);
        if (data is not { ValueKind: JsonValueKind.Object } page)
            return new LiPlaylist("Playlist", "", null, Array.Empty<LiSong>());

        var cover = Str(page, "cover");
        if (string.IsNullOrWhiteSpace(cover)) cover = Str(page, "thumbnail");
        var continuation = page.TryGetProperty("continuation", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString()
            : null;
        return new LiPlaylist(
            Str(page, "title"),
            cover,
            continuation,
            ParseSongs(page.TryGetProperty("items", out var items) ? items : null));
    }

    public async Task PlayAsync(JsonElement song) =>
        await InvokeAsync("play", new { item = song }).ConfigureAwait(false);

    public async Task PlayPlaylistAsync(IReadOnlyList<LiSong> songs, int start, string? sourceId, string? sourceName, string? continuation)
    {
        await InvokeAsync("play_playlist", new
        {
            items = songs.Select(s => s.Raw).ToArray(),
            start,
            sourceId,
            sourceName,
            shuffle = false,
            continuation,
        }).ConfigureAwait(false);
    }

    public async Task SetAutoplayAsync(bool on) =>
        await InvokeAsync("set_setting", new { key = "autoplay", value = on ? "true" : "false" }).ConfigureAwait(false);

    public async Task TogglePauseAsync() => await InvokeAsync("toggle_pause").ConfigureAwait(false);
    public async Task NextAsync() => await InvokeAsync("next_track").ConfigureAwait(false);
    public async Task PreviousAsync() => await InvokeAsync("prev_track").ConfigureAwait(false);
    public async Task SeekAsync(double seconds) => await InvokeAsync("seek", new { position = Math.Max(0, seconds) }).ConfigureAwait(false);
    public async Task ToggleShuffleAsync() => await InvokeAsync("toggle_shuffle").ConfigureAwait(false);
    public async Task SetRepeatAsync(string mode) => await InvokeAsync("set_repeat", new { mode }).ConfigureAwait(false);

    private static IReadOnlyList<LiSong> ParseSongs(JsonElement? data)
    {
        var list = new List<LiSong>();
        if (data is not { ValueKind: JsonValueKind.Array } array) return list;
        foreach (var item in array.EnumerateArray())
        {
            var id = Str(item, "video_id");
            var title = Str(item, "title");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title)) continue;
            list.Add(new LiSong(
                id,
                title,
                Str(item, "artists"),
                Str(item, "album"),
                Str(item, "duration"),
                item.Clone()));
        }
        return list;
    }

    private static string Str(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return "";
        if (!element.TryGetProperty(name, out var property)) return "";
        return property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : property.ToString();
    }

    public void Dispose()
    {
        try { _socket?.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait(500); } catch { }
        _socket?.Dispose();
        _socket = null;
    }
}
