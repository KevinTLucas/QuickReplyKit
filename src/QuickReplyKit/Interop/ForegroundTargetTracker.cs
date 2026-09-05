namespace QuickReplyKit.Interop;

/// <summary>
/// Shared one-slot holder for the window to paste into. The hotkey (and tray/--show path) writes
/// the foreground window captured at open time; <c>InsertService</c> reads it at insert time.
/// </summary>
public sealed class ForegroundTargetTracker
{
    public nint Target { get; set; }
}
