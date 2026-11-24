using System.Globalization;
using System.Text;
using MindAttic.Media.Capture.Hosting;
using MindAttic.Media.Capture.Tape;

namespace MindAttic.Media.Capture.Output;

/// <summary>Per-file sidecar (<c>file.ext.json</c>), serialized camelCase by <see cref="ICaptureJson"/>.</summary>
public sealed class CaptureSidecar
{
    /// <summary>Screenshot, Clip, Replay, Recording or Session.</summary>
    public string Kind { get; set; } = "";
    /// <summary>The caller's tag (also in the file name).</summary>
    public string Tag { get; set; } = "";
    /// <summary>The app the capture belongs to.</summary>
    public string App { get; set; } = "";
    /// <summary>The app's version, when known.</summary>
    public string? AppVersion { get; set; }
    /// <summary>The session id (its start timestamp).</summary>
    public string Session { get; set; } = "";
    /// <summary>What captured it: desktop, wpf, webview2, unity, playwright...</summary>
    public string Host { get; set; } = "";
    /// <summary>The machine it was captured on.</summary>
    public string Machine { get; set; } = Environment.MachineName;
    /// <summary>When it was captured.</summary>
    public DateTimeOffset Timestamp { get; set; }
    /// <summary>The event or rule that triggered it.</summary>
    public string? Trigger { get; set; }
    /// <summary>Clip length; 0 for a still.</summary>
    public double DurationSeconds { get; set; }
    /// <summary>Clip start on the session tape, in seconds.</summary>
    public double? TapeStart { get; set; }
    /// <summary>Clip end on the session tape, in seconds.</summary>
    public double? TapeEnd { get; set; }
    /// <summary>Width in pixels.</summary>
    public int? Width { get; set; }
    /// <summary>Height in pixels.</summary>
    public int? Height { get; set; }
    /// <summary>The encoding preset.</summary>
    public string? Preset { get; set; }
    /// <summary>The video encoder used.</summary>
    public string? Encoder { get; set; }
    /// <summary>Every file this capture produced (full paths).</summary>
    public List<string> Files { get; set; } = [];
    /// <summary>Markers inside the clip.</summary>
    public List<SidecarMarker> Markers { get; set; } = [];
    /// <summary>Extra data the caller attached.</summary>
    public Dictionary<string, object?>? Data { get; set; }
}

/// <summary>A marker inside a sidecar's clip, at an offset from the clip's start.</summary>
public sealed class SidecarMarker
{
    /// <summary>The marker's name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Seconds from the clip's start.</summary>
    public double OffsetSeconds { get; set; }
    /// <summary>Extra data the caller attached.</summary>
    public Dictionary<string, object?>? Data { get; set; }
}

/// <summary>{session start}.session.json: everything that happened in one app run.</summary>
internal sealed class SessionManifest
{
    /// <summary>The app the capture belongs to.</summary>
    public string App { get; set; } = "";
    /// <summary>The app's version, when known.</summary>
    public string? AppVersion { get; set; }
    /// <summary>The session id (its start timestamp).</summary>
    public string Session { get; set; } = "";
    /// <summary>What captured it: desktop, wpf, webview2, unity, playwright...</summary>
    public string Host { get; set; } = "";
    /// <summary>The machine it was captured on.</summary>
    public string Machine { get; set; } = Environment.MachineName;
    public DateTimeOffset Started { get; set; }
    public DateTimeOffset? Ended { get; set; }
    public string OutputRoot { get; set; } = "";
    public bool SessionRecording { get; set; }
    public string? TapeQuality { get; set; }
    public string? TapeSource { get; set; }
    /// <summary>The video encoder used.</summary>
    public string? Encoder { get; set; }
    public List<ManifestEvent> Events { get; set; } = [];
    public int EventsDropped { get; set; }
    public List<MarkerRecord> Markers { get; set; } = [];
    public List<ManifestCapture> Captures { get; set; } = [];
    public Dictionary<string, object?> Diagnostics { get; set; } = [];
}

internal sealed class ManifestEvent
{
    public string Name { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public double? TapeSeconds { get; set; }
    public Dictionary<string, object?>? Data { get; set; }
}

internal sealed class ManifestCapture
{
    /// <summary>Screenshot, Clip, Replay, Recording or Session.</summary>
    public string Kind { get; set; } = "";
    /// <summary>The caller's tag (also in the file name).</summary>
    public string Tag { get; set; } = "";
    public DateTimeOffset At { get; set; }
    /// <summary>Clip length; 0 for a still.</summary>
    public double DurationSeconds { get; set; }
    /// <summary>The event or rule that triggered it.</summary>
    public string? Trigger { get; set; }
    /// <summary>Every file this capture produced (full paths).</summary>
    public List<string> Files { get; set; } = [];
    public string? Error { get; set; }
}

/// <summary>Resolves the output root, owns the session folder, names files and writes sidecars and the manifest.</summary>
public sealed class OutputManager
{
    internal const int MaxManifestEvents = 10_000;

    private readonly CaptureOptions _options;
    private readonly ICaptureJson _json;
    private readonly ICaptureLog _log;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();
    private readonly object _manifestWriteGate = new();
    private readonly HashSet<string> _reserved = new(StringComparer.OrdinalIgnoreCase);
    private string? _root;
    private string? _sessionDir;
    private int _index;
    private long _bytesWritten;
    private long _baselineBytes = -1;
    private bool _diskWarned;

    internal OutputManager(CaptureOptions options, string host, ICaptureJson json, ICaptureLog log, Func<DateTimeOffset>? now = null)
    {
        _options = options;
        _json = json;
        _log = log;
        _now = now ?? (() => DateTimeOffset.Now);
        var started = _now();
        SessionId = CaptureNaming.Timestamp(started);
        Manifest = new SessionManifest
        {
            App = options.AppName, AppVersion = options.AppVersion, Session = SessionId, Host = host,
            Started = started, SessionRecording = options.SessionRecording,
        };
    }

    internal string SessionId { get; }
    internal SessionManifest Manifest { get; }
    internal event Action<double>? DiskWarning;

    /// <summary>The configured root, or the fallback (with a warning) when its drive is missing.</summary>
    internal string Root
    {
        get
        {
            lock (_gate)
            {
                if (_root is not null) return _root;
                _root = ResolveRoot(_options.OutputRoot, _options.FallbackRoot, _log);
                Manifest.OutputRoot = _root;
                return _root;
            }
        }
    }

    /// <summary>The configured root (created when missing), else the fallback with a warning; throws when neither can be used.</summary>
    public static string ResolveRoot(string configured, string fallback, ICaptureLog log)
    {
        if (TryCreate(configured, out var error)) return Path.GetFullPath(configured);
        log.Warn($"Output root \"{configured}\" is unavailable ({error}); using \"{fallback}\".");
        if (TryCreate(fallback, out var fallbackError)) return Path.GetFullPath(fallback);
        throw new CaptureException(CaptureError.OutputUnavailable,
            $"Neither \"{configured}\" ({error}) nor \"{fallback}\" ({fallbackError}) can be used for captures.");
    }

    private static bool TryCreate(string path, out string error)
    {
        error = "";
        try
        {
            var full = Path.GetFullPath(path);
            var drive = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(drive) || !Directory.Exists(drive))
            {
                error = $"drive {drive} not found";
                return false;
            }
            Directory.CreateDirectory(full);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>{root}\{app}\ — every session of this app gets its own folder underneath; created on first use.</summary>
    internal string AppDir
    {
        get
        {
            lock (_gate)
            {
                var dir = Path.Combine(Root, Sanitize(_options.AppName));
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
    }

    /// <summary>{timestamp}_{app}: shared by the session folder and every file's name inside it.</summary>
    internal string SessionName => $"{SessionId}_{Sanitize(_options.AppName)}";

    /// <summary>{app folder}\{timestamp}_{app}\ — this run's own folder: its video, screenshots, log and manifest.</summary>
    internal string SessionDir
    {
        get
        {
            lock (_gate)
            {
                if (_sessionDir is not null) return _sessionDir;
                var dir = Path.Combine(AppDir, SessionName);
                Directory.CreateDirectory(dir);
                _sessionDir = dir;
                return dir;
            }
        }
    }

    /// <summary>{session folder}\{timestamp}_{app}: the manifest ({stem}.session.json), log and stats.</summary>
    internal string SessionStem => Path.Combine(SessionDir, SessionName);

    /// <summary>{session folder}\{timestamp}_{app}_Video: the whole-session (or Start/StopRecording) export.</summary>
    internal string VideoStem => $"{SessionStem}_Video";

    /// <summary>Tape segments live here: on the output volume so finalizing never copies across drives.</summary>
    internal string StagingDir => Path.Combine(Root, ".staging", SessionName);

    /// <summary>Full path without extension for the next capture, from the file template.</summary>
    internal string NextStem(CaptureKind kind, string tag)
    {
        var dir = SessionDir;
        lock (_gate)
        {
            var index = _index++;
            var name = Expand(_options.FileTemplate, kind, tag, index);
            // Templates without {index}/{time} repeat names; suffix a counter instead of overwriting.
            for (var n = 1; ; n++)
            {
                var stem = Path.Combine(dir, n == 1 ? name : $"{name}_{n}");
                if (!_reserved.Contains(stem) && !Directory.EnumerateFiles(dir, Path.GetFileName(stem) + ".*").Any())
                {
                    _reserved.Add(stem);
                    return stem;
                }
            }
        }
    }

    internal string Expand(string template, CaptureKind kind, string tag, int index)
    {
        var now = _now();
        var sb = new StringBuilder(template);
        sb.Replace("{timestamp}", CaptureNaming.Timestamp(now))
          .Replace("{date}", now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
          .Replace("{time}", now.ToString("HHmmss_fff", CultureInfo.InvariantCulture))
          .Replace("{session}", SessionId)
          .Replace("{tag}", SanitizeTag(tag))
          .Replace("{index}", index.ToString("000", CultureInfo.InvariantCulture))
          .Replace("{kind}", kind.ToString())
          .Replace("{app}", Sanitize(_options.AppName));
        var name = Sanitize(sb.ToString());
        return name.Length > 0 ? name : $"capture_{index:000}";
    }

    /// <summary>Tags become [A-Za-z0-9._-]; everything else turns into '-'.</summary>
    public static string SanitizeTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return "capture";
        var chars = tag!.Trim().Select(c => char.IsLetterOrDigit(c) && c < 128 || c is '.' or '_' or '-' ? c : '-').ToArray();
        var s = new string(chars).Trim('-', '.');
        if (s.Length > 60) s = s.Substring(0, 60);
        return s.Length == 0 ? "capture" : s;
    }

    /// <summary>A file-name-safe form of <paramref name="value"/>: invalid characters and whitespace become '_', at most 120 characters.</summary>
    public static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '_' : c).ToArray();
        var s = new string(chars).Trim('.', ' ');
        if (s.Length > 120) s = s.Substring(0, 120);
        return s;
    }

    internal void WriteSidecar(string primaryFile, CaptureSidecar sidecar)
    {
        try
        {
            File.WriteAllText(primaryFile + ".json", _json.Serialize(sidecar));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"Could not write sidecar for {primaryFile}.", ex);
        }
    }

    internal void AddEvent(ManifestEvent e)
    {
        lock (_gate)
        {
            if (Manifest.Events.Count >= MaxManifestEvents) Manifest.EventsDropped++;
            else Manifest.Events.Add(e);
        }
    }

    internal void AddMarker(MarkerRecord m)
    {
        lock (_gate) Manifest.Markers.Add(m);
    }

    internal List<MarkerRecord> MarkersSnapshot()
    {
        lock (_gate) return Manifest.Markers.ToList();
    }

    internal void AddCapture(CaptureResult r)
    {
        lock (_gate)
        {
            Manifest.Captures.Add(new ManifestCapture
            {
                Kind = r.Kind.ToString().ToLowerInvariant(), Tag = r.Tag, At = r.Timestamp, DurationSeconds = r.Duration.TotalSeconds,
                Trigger = r.Trigger, Files = r.Files.ToList(), Error = r.Error,
            });
        }
        foreach (var f in r.Files) TrackBytes(f);
        SaveManifest();
    }

    internal void SaveManifest()
    {
        // Snapshot and write under one lock, so concurrent saves reach the file in snapshot order and never collide on it.
        lock (_manifestWriteGate)
        {
            string json;
            lock (_gate)
            {
                if (_sessionDir is null && Manifest.Captures.Count == 0 && Manifest.Markers.Count == 0 && !Manifest.SessionRecording) return;
                json = _json.Serialize(Manifest);
            }
            try
            {
                File.WriteAllText(SessionStem + ".session.json", json);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CaptureException)
            {
                _log.Warn("Could not write the session manifest.", ex);
            }
        }
    }

    private void TrackBytes(string file)
    {
        if (_options.DiskWarnGB is not { } limit) return;
        double totalGb;
        bool shouldWarn;
        try
        {
            lock (_gate)
            {
                _bytesWritten += new FileInfo(file).Length;
                if (_baselineBytes < 0)
                    _baselineBytes = DirectorySize(AppDir) - _bytesWritten;
                totalGb = (_baselineBytes + _bytesWritten) / 1e9;
                shouldWarn = totalGb > limit && !_diskWarned;
                if (shouldWarn) _diskWarned = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        if (shouldWarn)
        {
            _log.Warn($"Captures for {_options.AppName} use {totalGb:0.0} GB (warning threshold {limit:0.#} GB). Nothing is deleted automatically.");
            DiskWarning?.Invoke(totalGb);
        }
    }

    private static long DirectorySize(string dir) =>
        Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0;
}
