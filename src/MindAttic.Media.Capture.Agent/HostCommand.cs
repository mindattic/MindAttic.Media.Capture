using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MindAttic.Media.Capture.Hosting;
using MindAttic.Media.Capture.Windows;

namespace MindAttic.Media.Capture.Agent;

/// <summary>
/// <c>capture host --hwnd 0x.. --pid N --app Name [--version V] [--defaults path.json] --cmdline "..."</c>: captures an
/// engine game that cannot load .NET (Unreal), the way CaptureUnity does inside a Unity player. Builds the options from the
/// game's own command line, starts an engine on its window, then reads one JSON request per stdin line and writes one JSON
/// reply or notice per stdout line (docs/design/engine-host.md). Stops and exports on <c>{"op":"quit"}</c>, stdin EOF, or
/// the game's process exiting. Ported from SpaceGod.Unreal's Capture/SpaceGod.Capture.
/// </summary>
internal sealed class HostCommand(TextWriter output)
{
    private readonly object _writeLock = new();

    public static Task<int> RunAsync(AgentArgs args)
    {
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        return new HostCommand(stdout).RunAsync(args, stdin);
    }

    internal async Task<int> RunAsync(AgentArgs args, TextReader input)
    {
        var log = new LineLog(this);
        var (options, captureArgs) = BuildOptions(args, log);
        // -capture followed by no mode (or no -capture at all): nothing starts, as CaptureUnity.Args.Any false.
        if (!captureArgs.Any)
        {
            Send(new { op = "ready", any = false });
            return 0;
        }
        if (!args.Has("hwnd")) throw new CaptureException(CaptureError.InvalidArgument, "host needs --hwnd.");
        var target = args.Target();

        CaptureEngine engine;
        try
        {
            engine = await CaptureEngine.StartNewAsync(new DesktopCaptureHost(target, args.HostOptions()), options);
        }
        catch (CaptureException ex)
        {
            Send(new { op = "ready", any = true, enabled = false, error = ex.Message });
            return (int)ex.Error;
        }

        await using (engine)
        {
            Send(new { op = "ready", any = true, enabled = engine.Enabled, folder = engine.SessionFolder, session = engine.SessionId,
                fps = options.Fps, source = "window" });
            var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (args.GetInt("pid") is { } pid) WatchProcess(pid, stop);
            _ = Task.Run(async () =>
            {
                while (await input.ReadLineAsync() is { } line)
                {
                    if (line.Length == 0) continue;
                    if (!await HandleAsync(engine, line)) break;
                }
                stop.TrySetResult();
            });
            await stop.Task;
            log.Log(CaptureLogLevel.Info, "Game gone or asked to stop; exporting the session.");
        }
        Send(new { op = "stopped" });
        return 0;
    }

    /// <summary>
    /// CaptureUnity.BuildOptions for a game beside the host: the app's name and version, the game's <c>-capture</c> flags,
    /// and its shipped defaults file only when the command line names no <c>-capture-config</c>.
    /// </summary>
    internal static (CaptureOptions Options, CaptureArgs Args) BuildOptions(AgentArgs args, ICaptureLog log)
    {
        var options = new CaptureOptions { AppName = args.Get("app") ?? "Game", AppVersion = args.Get("version"), Log = log };
        var captureArgs = CaptureArgs.Parse(SplitCommandLine(args.Get("cmdline") ?? ""));
        if (captureArgs.Ignored != null)
            log.Log(CaptureLogLevel.Info, $"\"-capture {captureArgs.Ignored}\" is not a capture mode (video, screen N); left for the game.");
        if (!captureArgs.Any) return (options, captureArgs);
        options.Apply(captureArgs);
        if (captureArgs.ConfigPath == null && args.Get("defaults") is { Length: > 0 } defaults)
        {
            try
            {
                if (File.Exists(defaults)) options.ApplyConfigFile(defaults);
                else log.Log(CaptureLogLevel.Warning, $"[capture] no {Path.GetFileName(defaults)} at {defaults}: no default rules");
            }
            catch (CaptureException ex)
            {
                log.Log(CaptureLogLevel.Error, $"[capture] {Path.GetFileName(defaults)} not applied: {ex.Message}");
            }
            // The replay ring stays armed only when the session records video.
            if (!options.SessionRecording) options.ReplayArmed = false;
        }
        // The game reads its capture hotkeys through its own input and sends them as commands; a capture.json "hotkeys"
        // map is never bound as global Windows hotkeys.
        options.Hotkeys.Clear();
        options.DetachedFinalizerPath = Environment.ProcessPath;
        return (options, captureArgs);
    }

    private static void WatchProcess(int pid, TaskCompletionSource stop)
    {
        try
        {
            var game = Process.GetProcessById(pid);
            game.EnableRaisingEvents = true;
            game.Exited += (_, _) => stop.TrySetResult();
            if (game.HasExited) stop.TrySetResult();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            stop.TrySetResult();
        }
    }

    /// <summary>One request line; false for quit. A bad or unknown request is answered with an error <c>result</c>.</summary>
    internal async Task<bool> HandleAsync(CaptureEngine engine, string line)
    {
        int? id = null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var body = doc.RootElement;
            if (body.ValueKind != JsonValueKind.Object) throw new FormatException("A request is a JSON object.");
            string? Str(string name) => body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            if (body.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number) id = idEl.GetInt32();
            var op = Str("op") ?? "";
            switch (op)
            {
                case "raise":
                    engine.Raise(Str("event") ?? throw new ArgumentException("raise needs \"event\"."), Data(body));
                    return true;
                case "marker":
                    engine.Marker(Str("name") ?? throw new ArgumentException("marker needs \"name\"."), Data(body));
                    return true;
                case "screenshot":
                {
                    var shotId = id ?? 0;
                    // Answered when written, so the game can wait on its shot.
                    _ = engine.ScreenshotAsync(Str("tag") ?? "screenshot", new ScreenshotOptions { Data = Data(body) }).ContinueWith(t =>
                    {
                        var r = t.IsCompletedSuccessfully ? t.Result : null;
                        Send(new { op = "result", id = shotId, ok = r?.Succeeded ?? false, files = r?.Files ?? [], error = r?.Error ?? t.Exception?.GetBaseException().Message });
                    }, TaskScheduler.Default);
                    return true;
                }
                case "command":
                    _ = engine.ExecuteCommandAsync(Str("command") ?? "");
                    return true;
                case "note":
                    AppendNote(engine.SessionFolder, Str("text") ?? "");
                    return true;
                case "quit":
                    return false;
                default:
                    throw new ArgumentException($"Unknown op \"{op}\".");
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        {
            Send(new { op = "result", id, ok = false, error = ex.Message });
            return true;
        }
    }

    /// <summary>A line of the game's own log, kept beside the session as &lt;session folder&gt;.game.log (a Shipping game writes no log).</summary>
    private static void AppendNote(string folder, string text)
    {
        try
        {
            File.AppendAllText(Path.Combine(folder, Path.GetFileName(folder.TrimEnd('\\', '/')) + ".game.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + text + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static CaptureData? Data(JsonElement body)
    {
        if (!body.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Object) return null;
        var data = new CaptureData();
        foreach (var p in d.EnumerateObject())
        {
            data[p.Name] = p.Value.ValueKind switch
            {
                JsonValueKind.Number => p.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => p.Value.GetString(),
                JsonValueKind.Null => null,
                _ => p.Value.ToString(),
            };
        }
        return data;
    }

    private void Send(object message)
    {
        var json = JsonSerializer.Serialize(message);
        lock (_writeLock) output.WriteLine(json);
    }

    /// <summary>Windows command-line splitting (CommandLineToArgvW's quote and backslash rules).</summary>
    internal static List<string> SplitCommandLine(string text)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        var any = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\')
            {
                var n = 0;
                while (i < text.Length && text[i] == '\\') { n++; i++; }
                if (i < text.Length && text[i] == '"')
                {
                    sb.Append('\\', n / 2);
                    if (n % 2 == 1) sb.Append('"');
                    else inQuotes = !inQuotes;
                }
                else
                {
                    sb.Append('\\', n);
                    i--;
                }
                any = true;
                continue;
            }
            if (c == '"') { inQuotes = !inQuotes; any = true; continue; }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (any || sb.Length > 0) result.Add(sb.ToString());
                sb.Clear();
                any = false;
                continue;
            }
            sb.Append(c);
            any = true;
        }
        if (any || sb.Length > 0) result.Add(sb.ToString());
        return result;
    }

    /// <summary>Engine log lines go to the game, which writes them to its own log.</summary>
    private sealed class LineLog(HostCommand host) : ICaptureLog
    {
        public void Log(CaptureLogLevel level, string message, Exception? exception = null)
        {
            if (level < CaptureLogLevel.Info) return;
            host.Send(new { op = "log", level = level.ToString(), message = exception is null ? message : message + " " + exception.Message });
        }
    }
}
