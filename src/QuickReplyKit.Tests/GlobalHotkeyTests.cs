using System.Threading;
using QuickReplyKit.Interop;

namespace QuickReplyKit.Tests;

/// <summary>
/// Integration tests for <see cref="GlobalHotkey"/> against the real Win32 RegisterHotKey: a bad
/// gesture and an already-taken gesture both return false (never throw), Dispose frees the binding
/// so it can be re-registered, and Rebind can move between gestures without leaking. Uses an obscure
/// combo (Ctrl+Alt+Shift+F24) unlikely to collide with anything on the machine. GlobalHotkey builds
/// an HwndSource, so every body runs on an STA thread.
/// </summary>
public class GlobalHotkeyTests
{
    private const string Combo = "Ctrl+Alt+Shift+F24";

    private static void Sta(Action test)
    {
        Exception? captured = null;
        var t = new Thread(() =>
        {
            try { test(); }
            catch (Exception e) { captured = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        t.Join();
        if (captured != null) throw new Exception("STA test body threw", captured);
    }

    [Fact]
    public void Rebind_BadGesture_ReturnsFalse_NoThrow() => Sta(() =>
    {
        using var hk = new GlobalHotkey();
        Assert.False(hk.Rebind("not a real gesture!!"));
        Assert.False(hk.Rebind(""));
    });

    [Fact]
    public void Rebind_GoodGesture_Succeeds_And_Dispose_Unregisters() => Sta(() =>
    {
        var hk = new GlobalHotkey();
        Assert.True(hk.Rebind(Combo));
        hk.Dispose();   // must unregister so the combo is free again

        // A fresh hotkey can now take the same combo — proves Dispose released it.
        using var hk2 = new GlobalHotkey();
        Assert.True(hk2.Rebind(Combo));
    });

    [Fact]
    public void Rebind_AlreadyTaken_ReturnsFalse_NoThrow() => Sta(() =>
    {
        using var first = new GlobalHotkey();
        Assert.True(first.Rebind(Combo));

        using var second = new GlobalHotkey();
        Assert.False(second.Rebind(Combo));   // taken by `first`, must not throw
    });

    [Fact]
    public void Rebind_Twice_MovesBinding_NoLeak() => Sta(() =>
    {
        using var hk = new GlobalHotkey();
        Assert.True(hk.Rebind("Ctrl+Alt+Shift+F23"));
        Assert.True(hk.Rebind("Ctrl+Alt+Shift+F22"));   // re-bind releases the previous one

        // The first gesture is free again after the move.
        using var other = new GlobalHotkey();
        Assert.True(other.Rebind("Ctrl+Alt+Shift+F23"));
    });
}
