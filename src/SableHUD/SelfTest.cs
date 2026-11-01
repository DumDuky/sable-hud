using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SableHUD.Core;
using SableHUD.Input;
using SableHUD.Media;
using SableHUD.SystemInfo;

namespace SableHUD;

/// <summary>
/// Headless diagnostic pass. Writes a JSON report so the checks can be inspected
/// without a console attached (Sable is a WinExe).
/// </summary>
public static class SelfTest
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    private static readonly List<object> Checks = new();

    public static bool Run(string[] args)
    {
        try { AttachConsole(-1); } catch { }
        Checks.Clear();

        var path = ReportPath(args);
        var started = Stopwatch.StartNew();

        Check("os", true, $"{Environment.OSVersion} · {RuntimeInformation.OSArchitecture}");
        Check("runtime", true, $".NET {Environment.Version} · {RuntimeInformation.ProcessArchitecture}");
        Check("version", true, typeof(SelfTest).Assembly.GetName().Version?.ToString() ?? "1.0.0");

        // --- themes + resources -------------------------------------------------
        var themeNames = new List<string>();
        foreach (var theme in Themes.All)
        {
            ThemeManager.Apply(theme.Id);
            themeNames.Add(ThemeManager.Current.Id);
        }
        var distinct = themeNames.Distinct().Count();
        ThemeManager.Apply("Noir");
        Check("themes", distinct == 4, $"applied {string.Join(", ", themeNames)}");

        var resourcesOk = Application.Current.Resources["Text"] is Brush
                          && Application.Current.Resources["IconPlay"] is Geometry
                          && Application.Current.Resources["IconMusic"] is Geometry;
        Check("resources", resourcesOk, "brushes + vector icons resolved");

        var iconOk = false;
        try
        {
            var icon = Application.GetResourceStream(Assets.IconUri);
            iconOk = icon is not null && icon.Stream.Length > 0;
            icon?.Stream.Dispose();
        }
        catch { }
        Check("icon", iconOk, "packed app.ico loaded");

        // --- settings round-trip ------------------------------------------------
        var settingsOk = false;
        string settingsDetail;
        try
        {
            var probe = new AppSettings
            {
                Theme = "Ivory",
                Scale = "Large",
                Trigger = "GuideHold",
                CustomTrigger = "LB+RB",
                AutoHideSeconds = 20,
                MusicFolder = @"C:\Music",
                ArtworkSource = "Playlist",
                Autoplay = false,
            };
            var json = JsonSerializer.Serialize(probe, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            });
            var back = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            settingsOk = back is not null
                         && back.Theme == "Ivory"
                         && back.Scale == "Large"
                         && back.Trigger == "GuideHold"
                         && back.AutoHideSeconds == 20
                         && back.ArtworkSource == "Playlist"
                         && back.Autoplay == false;
            settingsDetail = $"round-trip {json.Length} bytes · theme {back?.Theme}, trigger {back?.Trigger}";
        }
        catch (Exception ex)
        {
            settingsDetail = ex.Message;
        }
        Check("settings", settingsOk, settingsDetail);

        // --- trigger parsing ----------------------------------------------------
        var guide = ControllerRouter.ParseMask("Guide");
        var chord = ControllerRouter.ParseMask("LB+RB");
        var viewMenu = ControllerRouter.ParseMask("Back+Start");
        Check("trigger-parse",
            guide == (ushort)PadButton.Guide && chord == 0x0300 && viewMenu == 0x0030,
            $"Guide=0x{guide:X}, LB+RB=0x{chord:X}, View+Menu=0x{viewMenu:X}");

        // --- trigger state machine ----------------------------------------------
        var triggerOk = false;
        var triggerDetail = "";
        try
        {
            var padStub = new XInputService();
            var guideSettings = new AppSettings { Trigger = "Guide" };
            var guideRouter = new ControllerRouter(padStub, guideSettings);
            var toggles = 0;
            guideRouter.ToggleHud += () => toggles++;
            guideRouter.InjectForTest((ushort)PadButton.Guide, 0);
            guideRouter.InjectForTest(0, (ushort)PadButton.Guide);
            Thread.Sleep(400);
            guideRouter.InjectForTest((ushort)PadButton.Guide, 0);
            guideRouter.Dispose();

            var doubleSettings = new AppSettings { Trigger = "GuideDouble" };
            var doubleRouter = new ControllerRouter(padStub, doubleSettings);
            var doubleToggles = 0;
            doubleRouter.ToggleHud += () => doubleToggles++;
            doubleRouter.InjectForTest((ushort)PadButton.Guide, 0);
            doubleRouter.InjectForTest(0, (ushort)PadButton.Guide);
            Thread.Sleep(240);
            doubleRouter.InjectForTest((ushort)PadButton.Guide, 0);
            doubleRouter.InjectForTest(0, (ushort)PadButton.Guide);
            doubleRouter.Dispose();

            var chordSettings = new AppSettings { Trigger = "LbRb" };
            var chordRouter = new ControllerRouter(padStub, chordSettings);
            var chordToggles = 0;
            chordRouter.ToggleHud += () => chordToggles++;
            chordRouter.InjectForTest((ushort)PadButton.LeftBumper, 0);
            chordRouter.InjectForTest((ushort)PadButton.RightBumper, 0);
            chordRouter.Dispose();
            padStub.Dispose();

            triggerOk = toggles == 2 && doubleToggles == 1 && chordToggles == 1;
            triggerDetail = $"single Guide toggles={toggles} (expected 2) · double Guide={doubleToggles} (expected 1) · LB+RB={chordToggles} (expected 1)";
        }
        catch (Exception ex)
        {
            triggerDetail = ex.Message;
        }
        Check("trigger-logic", triggerOk, triggerDetail);

        // --- controller mapping (A/B/X/Y, D-pad, stick, triggers) ---------------
        var mapOk = false;
        var mapDetail = "";
        try
        {
            var padStub = new XInputService();
            var router = new ControllerRouter(padStub, new AppSettings()) { IsHudVisible = () => true };
            var a = 0; var b = 0; var x = 0; var y = 0;
            var left = 0; var right = 0; var up = 0; var down = 0; var adjustUp = 0; var adjustDown = 0;
            router.Confirm += () => a++;
            router.Back += () => b++;
            router.Secondary += () => x++;
            router.MiniPlayer += () => y++;
            router.Navigate += d =>
            {
                switch (d)
                {
                    case NavDir.Left: left++; break;
                    case NavDir.Right: right++; break;
                    case NavDir.Up: up++; break;
                    default: down++; break;
                }
            };
            router.Adjust += d => { if (d > 0) adjustDown++; else adjustUp++; };

            router.InjectForTest((ushort)PadButton.A, 0);
            router.InjectForTest((ushort)PadButton.B, 0);
            router.InjectForTest((ushort)PadButton.X, 0);
            router.InjectForTest((ushort)PadButton.Y, 0);
            router.InjectForTest((ushort)PadButton.DPadLeft, 0);
            router.InjectForTest((ushort)PadButton.DPadRight, 0);
            router.InjectForTest((ushort)PadButton.DPadUp, 0);
            router.InjectForTest((ushort)PadButton.DPadDown, 0);
            router.InjectTickForTest(new PadState(0, -30000, 0, 0, 0, 0, 0));
            router.InjectTickForTest(new PadState(0, 30000, 0, 0, 0, 0, 0));
            router.InjectTickForTest(new PadState(0, 0, 0, 0, 0, 200, 200));
            router.Dispose();
            padStub.Dispose();

            mapOk = a == 1 && b == 1 && x == 1 && y == 1 &&
                    left == 2 && right == 2 && up == 1 && down == 1 &&
                    adjustUp == 1 && adjustDown == 1;
            mapDetail = $"A={a} B={b} X={x} Y={y} · D-pad L/R={left}/{right} U/D={up}/{down} · stick L/R={left - 1}/{right - 1} · LT/RT={adjustUp}/{adjustDown}";
        }
        catch (Exception ex)
        {
            mapDetail = ex.Message;
        }
        Check("controller-map", mapOk, mapDetail);

        // --- optional isolation driver -----------------------------------------
        Check("hidhide", true,
            HidHide.IsInstalled
                ? "driver installed"
                : (HidHide.InstallerPath is null ? "not installed; installer missing" : "not installed; bundled installer ready"));

        // --- paths --------------------------------------------------------------
        var pathsOk = false;
        try
        {
            var probe = Path.Combine(Paths.DataDir, "selftest.tmp");
            File.WriteAllText(probe, "ok");
            pathsOk = File.ReadAllText(probe) == "ok";
            File.Delete(probe);
        }
        catch { }
        Check("paths", pathsOk, Paths.DataDir);

        // --- controller ---------------------------------------------------------
        var pad = new XInputService();
        pad.Start();
        Thread.Sleep(250);
        var padInfo = pad.Pad;
        Check("xinput-guide", XInputService.GuideButtonSupported,
            XInputService.GuideButtonSupported ? "XInputGetStateEx bound (Guide visible)" : "ordinal 100 unavailable; keyboard fallback");
        Check("controller", true,
            padInfo.Connected ? $"pad {padInfo.Slot + 1} · {padInfo.Battery} · wireless={padInfo.Wireless}" : "no pad attached (soft check)");
        pad.Dispose();

        // --- audio --------------------------------------------------------------
        var audio = new AudioService();
        var audioSnap = audio.Refresh();
        Check("audio", audioSnap.Master >= 0,
            $"master {audioSnap.Master * 100:0}% · muted={audioSnap.MasterMuted} · mic={(audioSnap.MicAvailable ? (audioSnap.MicMuted ? "muted" : "live") : "none")} · {audioSnap.Sessions.Count} sessions");

        // --- GPU counters -------------------------------------------------------
        using (var gpu = new GpuCounters())
        {
            Thread.Sleep(350);
            var (usage, vram) = gpu.Read();
            Check("gpu", gpu.Available, gpu.Available ? $"PDH ok · 3D {usage:0}% · VRAM {vram:0.0} GB" : "PDH GPU counters unavailable");
        }

        // --- system sample ------------------------------------------------------
        var monitor = new SystemMonitor();
        Thread.Sleep(1300);
        var sys = monitor.Current;
        Check("system", sys.RamTotalGb > 0,
            $"CPU {sys.CpuPercent:0}% · RAM {sys.RamUsedGb:0.0}/{sys.RamTotalGb:0.0} GB · focus '{sys.FocusTitle}' · net {sys.NetDownMbps:0.0}/{sys.NetUpMbps:0.0} Mb/s");
        monitor.Dispose();

        // --- media session ------------------------------------------------------
        var smtc = new SmtcService();
        smtc.Start();
        Thread.Sleep(1400);
        var media = smtc.Current;
        var mediaDetail = media.HasTrack
            ? $"{media.Title} — {media.Artist} [{media.Source}] · {media.Position:mm\\:ss}/{media.Duration:mm\\:ss} · playing={media.IsPlaying}"
            : "silent";
        Check("smtc", true,
            $"manager={(smtc.Ready ? "ready" : "unavailable")} · sessions={smtc.SessionCount} · current={mediaDetail}");
        smtc.Dispose();

        // --- LiMusic HUD bridge -------------------------------------------------
        var bridgeOk = false;
        var bridgeDetail = "";
        try
        {
            var bridge = new LiMusicBridge();
            var connected = PumpUntil(() => bridge.TryConnectAsync());
            if (connected)
            {
                var songs = PumpUntil(() => bridge.SearchAsync("daft punk"));
                var library = PumpUntil(() => bridge.GetLibraryAsync());
                var firstPlaylist = library.FirstOrDefault();
                var playlist = firstPlaylist is null ? null : PumpUntil(() => bridge.GetPlaylistAsync(firstPlaylist.Id));
                bridgeOk = songs.Count > 0;
                bridgeDetail = $"connected · {songs.Count} search results · {library.Count} playlists · \"{playlist?.Title}\" {playlist?.Songs.Count} tracks · cover={(string.IsNullOrEmpty(playlist?.CoverUrl) ? "none" : "yes")}";
            }
            else
            {
                bridgeDetail = bridge.Status;
            }
            bridge.Dispose();
        }
        catch (Exception ex)
        {
            bridgeDetail = ex.Message;
        }
        Check("limusic-bridge", bridgeOk, bridgeDetail);

        // --- local playback -----------------------------------------------------
        var localOk = false;
        var localDetail = "";
        var toneDir = Path.Combine(Path.GetTempPath(), "sable-selftest-tone");
        try
        {
            Directory.CreateDirectory(toneDir);
            var wavPath = Path.Combine(toneDir, "Sable Test Tone.wav");
            WriteTone(wavPath, 1.0);

            var local = new LocalPlayer();
            PumpUntil(local.ScanAsync(toneDir));
            var found = local.Library.Count;
            if (found > 0)
            {
                local.PlayIndex(0);
                var until = DateTime.UtcNow.AddSeconds(2);
                while (DateTime.UtcNow < until && local.Position.TotalMilliseconds < 120)
                    PumpFor(TimeSpan.FromMilliseconds(40));
                localOk = local.IsPlaying && local.Position.TotalMilliseconds > 100;
                localDetail = $"scanned {found} · state={(local.IsPlaying ? "playing" : "stopped")} · position {local.Position.TotalSeconds:0.00}s · \"{local.Current?.Title}\"";
                local.Dispose();
            }
            else
            {
                localDetail = "scan found no tracks";
            }
        }
        catch (Exception ex)
        {
            localDetail = ex.Message;
        }
        finally
        {
            try { Directory.Delete(toneDir, true); } catch { }
        }
        Check("local-player", localOk, localDetail);

        // --- optional RTSS ------------------------------------------------------
        using (var rtss = new RtssReader())
        {
            var foreground = WindowFx.ForegroundProcessId();
            var fps = rtss.ReadFps(foreground);
            Check("rtss", true, fps is null ? "RTSS not running (soft check)" : $"foreground FPS {fps:0.0}");
        }

        var critical = new[] { "themes", "resources", "icon", "settings", "trigger-parse", "trigger-logic", "controller-map", "paths", "xinput-guide" };
        var passed = Checks
            .OfType<Dictionary<string, object>>()
            .Where(c => critical.Contains((string)c["name"]))
            .All(c => (bool)c["ok"]);

        var report = new
        {
            app = "SableHUD",
            version = typeof(SelfTest).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            os = Environment.OSVersion.ToString(),
            arch = RuntimeInformation.OSArchitecture.ToString(),
            elapsedMs = started.ElapsedMilliseconds,
            passed,
            checks = Checks,
            log = Paths.LogFile,
        };

        var jsonOut = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, jsonOut);
        }
        catch { }

        try
        {
            Console.WriteLine();
            Console.WriteLine("Sable HUD self-test");
            foreach (var check in Checks.OfType<Dictionary<string, object>>())
                Console.WriteLine($"  [{((bool)check["ok"] ? "ok" : "!!")}] {check["name"]}: {check["detail"]}");
            Console.WriteLine($"  report: {path}");
            Console.WriteLine($"  result: {(passed ? "PASS" : "FAIL")}");
        }
        catch { }

        Log.Info($"selftest {(passed ? "PASS" : "FAIL")} · {path}");
        return passed;
    }

    private static string ReportPath(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--selftest", StringComparison.OrdinalIgnoreCase) &&
                !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                return Path.GetFullPath(args[i + 1]);

        return Path.Combine(Path.GetTempPath(), "sable-selftest.json");
    }

    private static void PumpUntil(Task task)
    {
        while (!task.IsCompleted)
            PumpFor(TimeSpan.FromMilliseconds(15));
        task.GetAwaiter().GetResult();
    }

    private static T PumpUntil<T>(Func<Task<T>> factory)
    {
        var task = factory();
        PumpUntil(task);
        return task.GetAwaiter().GetResult();
    }

    private static void PumpFor(TimeSpan duration)
    {
        var until = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < until)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(2);
        }
    }

    private static void WriteTone(string path, double seconds)
    {
        const int rate = 44100;
        var samples = (int)(rate * seconds);
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write("RIFF"u8.ToArray());
        bw.Write(36 + samples * 2);
        bw.Write("WAVE"u8.ToArray());
        bw.Write("fmt "u8.ToArray());
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)1);
        bw.Write(rate);
        bw.Write(rate * 2);
        bw.Write((short)2);
        bw.Write((short)16);
        bw.Write("data"u8.ToArray());
        bw.Write(samples * 2);
        for (var i = 0; i < samples; i++)
        {
            var t = i / (double)rate;
            var envelope = Math.Min(1.0, t * 20) * Math.Min(1.0, (seconds - t) * 8);
            var v = (short)(Math.Sin(2 * Math.PI * 440 * t) * 2400 * envelope);
            bw.Write(v);
        }
    }

    private static void Check(string name, bool ok, string detail)
    {
        Checks.Add(new Dictionary<string, object>
        {
            ["name"] = name,
            ["ok"] = ok,
            ["detail"] = detail,
        });
    }
}
