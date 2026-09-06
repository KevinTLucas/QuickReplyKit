namespace QuickReplyKit.Interop;

/// <summary>
/// Shared one-slot holder for the window to paste into. <see cref="ForegroundMonitor"/> keeps this
/// updated live with the last real (non-QuickReplyKit) foreground window, and the show-time capture in
/// <c>App</c> writes it too as a fallback; <c>InsertService</c> reads it at insert time.
/// </summary>
public sealed class ForegroundTargetTracker
{
    public nint Target { get; set; }
}
