using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MindAttic.Media.Capture.Windows;

namespace MindAttic.Media.Capture.Tray;

/// <summary>
/// Watches for starts of the registered apps set to record. While at least one is, it polls every 1.5 s for new
/// processes with that exe's name (no admin, no hooks), matches the full exe path, then waits for the process's
/// window (at least 100x100, seen on two polls so it has its real size) and raises <see cref="WindowReady"/>.
/// A process already running when its app starts being watched is not a start.
/// </summary>
internal sealed class AppWatcher : IDisposable
{
    private static readonly TimeSpan WindowTimeout = TimeSpan.FromMinutes(2);

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };
    private readonly Func<IReadOnlyList<WatchedApp>> _apps;
    // Exe paths whose already-running processes have been noted, so only later ones count as starts.
    private readonly HashSet<string> _baselined = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _seen = [];
    private readonly Dictionary<int, Pending> _pending = [];

    private sealed class Pending(string path)
    {
        public string Path { get; } = path;
        public Stopwatch Since { get; } = Stopwatch.StartNew();
        public nint Window { get; set; }
    }

    /// <summary>A watched app started and its window is up: exe path, process id, window.</summary>
    public event Action<string, int, WindowInfo>? WindowReady;

    public AppWatcher(Func<IReadOnlyList<WatchedApp>> apps)
    {
        _apps = apps;
        _timer.Tick += (_, _) => Poll();
        Refresh();
    }

    /// <summary>Call after the app list changes: notes what is already running at once, and polls only while needed.</summary>
    public void Refresh()
    {
        Poll();
        if (_baselined.Count > 0) _timer.Start();
        else _timer.Stop();
    }

    private void Poll()
    {
        var watched = _apps().Where(a => a.Watching && a.Path.Length > 0).Select(a => a.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var alive = new HashSet<int>();
        foreach (var name in watched.Select(Path.GetFileNameWithoutExtension).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    alive.Add(p.Id);
                    if (!_seen.Add(p.Id)) continue;
                    if (ExePath(p.Id) is { } path && watched.Contains(path) && _baselined.Contains(path))
                        _pending[p.Id] = new Pending(path);
                }
            }
        }
        _seen.IntersectWith(alive);
        _baselined.Clear();
        _baselined.UnionWith(watched);
        foreach (var pid in _pending.Keys.Where(pid => !alive.Contains(pid) || _pending[pid].Since.Elapsed > WindowTimeout).ToList())
            _pending.Remove(pid);
        if (_pending.Count == 0) return;

        var windows = Desktop.GetWindows();
        foreach (var (pid, pending) in _pending.ToList())
        {
            var window = windows.Where(w => w.ProcessId == pid && w.Bounds.Width >= 100 && w.Bounds.Height >= 100)
                .OrderByDescending(w => w.Bounds.Area).FirstOrDefault();
            if (window is null) continue;
            if (pending.Window != window.Hwnd)
            {
                pending.Window = window.Hwnd;
                continue;
            }
            _pending.Remove(pid);
            WindowReady?.Invoke(pending.Path, pid, window);
        }
    }

    /// <summary>The full path of <paramref name="pid"/>'s exe, or null when it can't be read (gone, or elevated).</summary>
    public static string? ExePath(int pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (handle == 0) return null;
        try
        {
            var text = new StringBuilder(1024);
            var size = (uint)text.Capacity;
            return QueryFullProcessImageName(handle, 0, text, ref size) ? text.ToString(0, (int)size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public void Dispose() => _timer.Dispose();

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder name, ref uint size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);
}
