using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace QuickReplyKit.Interop;

/// <summary>
/// A single system-wide hotkey backed by <c>RegisterHotKey</c> on an invisible message-only
/// window. On press it raises <see cref="Pressed"/> on the UI thread. Registration failure
/// (gesture already taken) is reported via the return value, never thrown — the app keeps running.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0x4B52;          // arbitrary, 'KR'
    private const uint MOD_NOREPEAT = 0x4000;      // don't autofire while held
    private static readonly nint HWND_MESSAGE = new(-3);

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hWnd, int id);

    private readonly HwndSource _source;
    private bool _registered;

    /// <summary>Raised on the UI thread when the hotkey is pressed.</summary>
    public event Action? Pressed;

    public GlobalHotkey()
    {
        _source = new HwndSource(new HwndSourceParameters("QuickReplyKit.Hotkey")
        {
            ParentWindow = HWND_MESSAGE,   // message-only window: no UI, just receives WM_HOTKEY
            WindowStyle = 0,
        });
        _source.AddHook(WndProc);
    }

    /// <summary>(Re)bind to <paramref name="gesture"/>. Returns false if it can't be parsed or is already taken.</summary>
    public bool Rebind(string gesture)
    {
        Unregister();
        if (!HotkeyGesture.TryParse(gesture, out var g)) return false;
        _registered = RegisterHotKey(_source.Handle, HOTKEY_ID, g.Modifiers | MOD_NOREPEAT, g.VirtualKey);
        return _registered;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam == HOTKEY_ID)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return 0;
    }

    private void Unregister()
    {
        if (_registered) { UnregisterHotKey(_source.Handle, HOTKEY_ID); _registered = false; }
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
