using System.Runtime.InteropServices;

namespace QuickReplyKit.Interop;

/// <summary>
/// Continuously records the last foreground window that is NOT one of ours, into a
/// <see cref="ForegroundTargetTracker"/>. This is the window we paste into — tracking it live means
/// it's correct no matter how the launcher is summoned (hotkey, tray, or a fresh launch), instead of
/// only sampling at show-time (which caught the launching shell instead of the user's real app).
///
/// Uses a WinEvent hook with WINEVENT_SKIPOWNPROCESS, so our own launcher/editor/dialog windows never
/// register as the target. Install on the UI thread (it has the message loop OUTOFCONTEXT delivery needs).
/// </summary>
public sealed class ForegroundMonitor : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    private delegate void WinEventProc(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(uint min, uint max, nint hmod, WinEventProc cb, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);

    private readonly ForegroundTargetTracker _tracker;
    private readonly WinEventProc _callback;   // kept alive so the GC doesn't collect the delegate
    private nint _hook;

    public ForegroundMonitor(ForegroundTargetTracker tracker)
    {
        _tracker = tracker;
        _callback = OnForeground;
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0,
            _callback, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    private void OnForeground(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // idObject == OBJID_WINDOW (0) filters out caret/menu/etc. sub-object events.
        if (hwnd != 0 && idObject == 0) _tracker.Target = hwnd;
    }

    public void Dispose()
    {
        if (_hook != 0) { UnhookWinEvent(_hook); _hook = 0; }
    }
}
