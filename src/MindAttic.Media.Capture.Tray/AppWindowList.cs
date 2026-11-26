using System.Runtime.InteropServices;
using MindAttic.Media.Capture.Windows;

namespace MindAttic.Media.Capture.Tray;

/// <summary>The windows offered by Record App / Screenshot App: other processes' visible windows, with their icons.</summary>
internal static class AppWindowList
{
    private const uint WM_GETICON = 0x007F;
    private const int ICON_SMALL2 = 2;
    private const int GCLP_HICONSM = -34;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    public static IReadOnlyList<WindowInfo> Get()
    {
        var self = Environment.ProcessId;
        return Desktop.GetWindows().Where(w => w.ProcessId != self && w.Bounds.Width > 1 && w.Bounds.Height > 1).ToList();
    }

    /// <summary>
    /// The top-level window that has focus right now (Unity, Unreal, a WPF app, a browser...), or null when that is
    /// the desktop, the taskbar or this tray itself. Its handle is what capture.exe's <c>--hwnd</c> records
    /// (<see cref="CaptureTarget.Window"/>, resolved by the same TargetResolver).
    /// </summary>
    public static WindowInfo? Focused()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0) return null;
        // A focused child or popup (a Unity/Unreal viewport, a WPF popup) records as its top-level window.
        var root = GetAncestor(hwnd, GA_ROOT);
        if (root != 0) hwnd = root;
        if (IsShell(hwnd)) return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId) return null;
        if (Desktop.GetWindows().FirstOrDefault(w => w.Hwnd == hwnd) is { } listed) return listed;
        var name = "";
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            name = p.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { }
        var title = new System.Text.StringBuilder(512);
        GetWindowText(hwnd, title, title.Capacity);
        return new WindowInfo(hwnd, title.ToString(), (int)pid, name, default);
    }

    private static bool IsShell(nint hwnd)
    {
        var cls = new System.Text.StringBuilder(64);
        GetClassName(hwnd, cls, cls.Capacity);
        return cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    public static string MenuText(WindowInfo w)
    {
        var title = w.Title.Length > 60 ? w.Title[..57] + "..." : w.Title;
        // '&' would turn the next letter into a menu mnemonic.
        return $"{title.Replace("&", "&&")}  ({w.ProcessName})";
    }

    /// <summary>The window's small icon, or null; a hung window can't stall the menu (100 ms timeout).</summary>
    public static Image? Icon(nint hwnd)
    {
        nint handle = 0;
        if (SendMessageTimeout(hwnd, WM_GETICON, ICON_SMALL2, 0, SMTO_ABORTIFHUNG, 100, out var result) != 0) handle = result;
        if (handle == 0) handle = GetClassLongPtr(hwnd, GCLP_HICONSM);
        if (handle == 0) return null;
        try
        {
            using var icon = System.Drawing.Icon.FromHandle(handle);
            return icon.ToBitmap();
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException)
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessageTimeout(nint hwnd, uint msg, nint wParam, nint lParam, uint flags, uint timeout, out nint result);

    private const uint GA_ROOT = 2;

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    private static extern int GetWindowText(nint hwnd, System.Text.StringBuilder text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(nint hwnd, System.Text.StringBuilder name, int max);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern nint GetClassLongPtr(nint hwnd, int index);
}
