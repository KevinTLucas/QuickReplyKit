using System.Runtime.InteropServices;
using System.Threading;

namespace QuickReplyKit.Interop;

/// <summary>
/// Capture and restore the foreground window across processes, working around Windows'
/// foreground lock via the AttachThreadInput trick. No hooks — RegisterHotKey only.
/// </summary>
internal static class ForegroundWindow
{
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hWnd, out uint pid);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint hWnd);

    public static nint Get() => GetForegroundWindow();

    public static bool IsAlive(nint hWnd) => hWnd != 0 && IsWindow(hWnd);

    /// <summary>
    /// Bring <paramref name="target"/> to the foreground and keep trying until it actually is
    /// foreground or ~200 ms elapses. To beat Windows' foreground lock we attach our input queue
    /// to BOTH the current-foreground thread and the target thread while calling
    /// SetForegroundWindow. Returns whether the target ended up foreground; callers paste regardless.
    /// </summary>
    public static bool Restore(nint target)
    {
        if (!IsAlive(target)) return false;

        uint ourThread = GetCurrentThreadId();
        uint targetThread = GetWindowThreadProcessId(target, out _);

        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (GetForegroundWindow() == target) return true;

            uint foreThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            bool aFore = foreThread != 0 && foreThread != ourThread && AttachThreadInput(ourThread, foreThread, true);
            bool aTarget = targetThread != ourThread && targetThread != foreThread && AttachThreadInput(ourThread, targetThread, true);
            try
            {
                BringWindowToTop(target);
                SetForegroundWindow(target);
            }
            finally
            {
                if (aTarget) AttachThreadInput(ourThread, targetThread, false);
                if (aFore) AttachThreadInput(ourThread, foreThread, false);
            }

            if (GetForegroundWindow() == target) return true;
            Thread.Sleep(20);
        }
        return GetForegroundWindow() == target;
    }
}
