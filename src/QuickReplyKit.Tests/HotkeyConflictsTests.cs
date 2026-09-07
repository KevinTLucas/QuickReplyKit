using QuickReplyKit.Core.Storage;
using QuickReplyKit.Interop;

namespace QuickReplyKit.Tests;

/// <summary>
/// Unit tests for <see cref="HotkeyConflicts"/> — the pure, OS-free hotkey analysis: order/case
/// independent key normalisation, duplicate detection (incl. a bind equal to the launcher), and
/// the manager's search-by-key lookup (Free / Launcher / Response).
/// </summary>
public class HotkeyConflictsTests
{
    private static ResponseHotkey Bind(string rp, string gesture, bool plain = false) =>
        new() { RelativePath = rp, Gesture = gesture, Plain = plain };

    // --- KeyOf: order/case independence -------------------------------------

    [Fact]
    public void KeyOf_IsOrderAndCaseIndependent()
    {
        Assert.Equal(HotkeyConflicts.KeyOf("Ctrl+Alt+D"), HotkeyConflicts.KeyOf("Alt+Ctrl+d"));
    }

    [Fact]
    public void KeyOf_ReturnsNull_ForUnparseable()
    {
        Assert.Null(HotkeyConflicts.KeyOf("Ctrl+"));
        Assert.Null(HotkeyConflicts.KeyOf(""));
        Assert.Null(HotkeyConflicts.KeyOf(null));
    }

    // --- Duplicates ---------------------------------------------------------

    [Fact]
    public void Duplicates_FlagsTwoBindsOnSameCombo_OrderIndependent()
    {
        var dupes = HotkeyConflicts.Duplicates("Ctrl+Alt+D",
            new[] { Bind("a.json", "Ctrl+Alt+1"), Bind("b.json", "Alt+Ctrl+1") });

        Assert.Contains(HotkeyConflicts.KeyOf("Ctrl+Alt+1")!.Value, dupes);
        Assert.DoesNotContain(HotkeyConflicts.KeyOf("Ctrl+Alt+D")!.Value, dupes);
    }

    [Fact]
    public void Duplicates_FlagsBindEqualToLauncher()
    {
        var dupes = HotkeyConflicts.Duplicates("Ctrl+Alt+D", new[] { Bind("a.json", "Alt+Ctrl+D") });
        Assert.Contains(HotkeyConflicts.KeyOf("Ctrl+Alt+D")!.Value, dupes);
    }

    [Fact]
    public void Duplicates_EmptyWhenAllDistinct()
    {
        var dupes = HotkeyConflicts.Duplicates("Ctrl+Alt+D",
            new[] { Bind("a.json", "Ctrl+Alt+1"), Bind("b.json", "Ctrl+Alt+2") });
        Assert.Empty(dupes);
    }

    [Fact]
    public void Duplicates_IgnoresUnparseableGestures()
    {
        var dupes = HotkeyConflicts.Duplicates("Ctrl+Alt+D",
            new[] { Bind("a.json", "garbage"), Bind("b.json", "also+bad+") });
        Assert.Empty(dupes);
    }

    // --- Resolve (search-by-key lookup) -------------------------------------

    [Fact]
    public void Resolve_Free_WhenNothingOwnsIt()
    {
        var hit = HotkeyConflicts.Resolve("Ctrl+Alt+9", "Ctrl+Alt+D", new[] { Bind("a.json", "Ctrl+Alt+1") });
        Assert.Equal(HotkeyConflicts.TargetKind.Free, hit.Kind);
        Assert.Null(hit.RelativePath);
    }

    [Fact]
    public void Resolve_Launcher_OrderIndependent()
    {
        var hit = HotkeyConflicts.Resolve("Alt+Ctrl+D", "Ctrl+Alt+D", Array.Empty<ResponseHotkey>());
        Assert.Equal(HotkeyConflicts.TargetKind.Launcher, hit.Kind);
    }

    [Fact]
    public void Resolve_Response_ReturnsRelativePath()
    {
        var hit = HotkeyConflicts.Resolve("Ctrl+Alt+1", "Ctrl+Alt+D",
            new[] { Bind("folder/greeting.json", "Alt+Ctrl+1") });
        Assert.Equal(HotkeyConflicts.TargetKind.Response, hit.Kind);
        Assert.Equal("folder/greeting.json", hit.RelativePath);
    }

    [Fact]
    public void Resolve_LauncherWinsTieOverResponse()
    {
        var hit = HotkeyConflicts.Resolve("Ctrl+Alt+D", "Ctrl+Alt+D",
            new[] { Bind("a.json", "Ctrl+Alt+D") });
        Assert.Equal(HotkeyConflicts.TargetKind.Launcher, hit.Kind);
    }

    [Fact]
    public void Resolve_Free_ForUnparseableGesture()
    {
        var hit = HotkeyConflicts.Resolve("Ctrl+", "Ctrl+Alt+D", Array.Empty<ResponseHotkey>());
        Assert.Equal(HotkeyConflicts.TargetKind.Free, hit.Kind);
    }
}
