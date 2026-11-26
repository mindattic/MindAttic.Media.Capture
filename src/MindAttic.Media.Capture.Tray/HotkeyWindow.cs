using System.Runtime.InteropServices;
using MindAttic.Media.Capture.Windows;

namespace MindAttic.Media.Capture.Tray;

/// <summary>
/// Record toggles the armed target (Screen 1 until something else is recorded or armed); RecordFocused toggles a
/// lossless recording of whichever window has focus when it is pressed; Screenshot shoots the armed target.
/// </summary>
internal enum TrayHotkey { Record = 1, Screenshot = 2, RecordFocused = 3 }

/// <summary>System-wide hotkeys delivered on the UI thread through a hidden message window.</summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private readonly HashSet<int> _registered = [];

    public HotkeyWindow() => CreateHandle(new CreateParams());

    public event Action<TrayHotkey>? Pressed;

    /// <summary>Binds <paramref name="key"/> (e.g. "Ctrl+Alt+Shift+R"); null when it worked, else why not.</summary>
    public string? Register(TrayHotkey hotkey, string key)
    {
        Unregister(hotkey);
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (!GlobalHotkeys.TryParse(key, out var mods, out var vk)) return $"\"{key}\" is not a key this recorder understands.";
        if (!RegisterHotKey(Handle, (int)hotkey, mods | MOD_NOREPEAT, vk)) return $"{key} is already used by another app.";
        _registered.Add((int)hotkey);
        return null;
    }

    public void Unregister(TrayHotkey hotkey)
    {
        if (_registered.Remove((int)hotkey)) UnregisterHotKey(Handle, (int)hotkey);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && _registered.Contains((int)m.WParam)) Pressed?.Invoke((TrayHotkey)(int)m.WParam);
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (var id in _registered.ToList()) UnregisterHotKey(Handle, id);
        _registered.Clear();
        DestroyHandle();
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
}
