using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace QuickReplyKit.Interop;

/// <summary>
/// Registers many system-wide hotkeys at once on a single invisible message-only window, each with
/// a unique id, and dispatches <c>WM_HOTKEY</c> to the bound action on the UI thread. Supersedes the
/// single-hotkey helper: the launcher hotkey and every per-response instant-paste bind share one
/// registrar. Registration failure (a combo already owned by another app, or a duplicate of one we
/// already hold) is reported by the <see cref="Register"/> return value — never thrown.
/// </summary>
public sealed class HotkeyRegistrar : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;      // don't autofire while held
    private static readonly nint HWND_MESSAGE = new(-3);

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hWnd, int id);

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 0x4B52;   // arbitrary base, 'KR'

    public HotkeyRegistrar()
    {
        _source = new HwndSource(new HwndSourceParameters("QuickReplyKit.Hotkeys")
        {
            ParentWindow = HWND_MESSAGE,   // message-only window: no UI, just receives WM_HOTKEY
            WindowStyle = 0,
        });
        _source.AddHook(WndProc);
    }

    /// <summary>
    /// Try to register <paramref name="gesture"/>; on success wires <paramref name="onPressed"/>
    /// (raised on the UI thread) and returns true. Returns false if the combo is unavailable —
    /// the caller keeps running and can surface the conflict.
    /// </summary>
    public bool Register(HotkeyGesture gesture, Action onPressed)
    {
        int id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, gesture.Modifiers | MOD_NOREPEAT, gesture.VirtualKey))
            return false;
        _actions[id] = onPressed;
        return true;
    }

    /// <summary>Release every registered hotkey (the HwndSource is reusable afterwards).</summary>
    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _actions.TryGetValue((int)wParam, out var action))
        {
            handled = true;
            action();
        }
        return 0;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
