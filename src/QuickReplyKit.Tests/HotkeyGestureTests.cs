using System.Windows.Input;
using QuickReplyKit.Interop;

namespace QuickReplyKit.Tests;

/// <summary>Unit tests for the <see cref="HotkeyGesture"/> parser — valid gestures and rejections.</summary>
public class HotkeyGestureTests
{
    private const uint Ctrl = HotkeyGesture.MOD_CONTROL;
    private const uint Alt = HotkeyGesture.MOD_ALT;
    private const uint Shift = HotkeyGesture.MOD_SHIFT;
    private const uint Win = HotkeyGesture.MOD_WIN;

    [Fact]
    public void Parses_CtrlAltSpace()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+Alt+Space", out var g));
        Assert.Equal(Ctrl | Alt, g.Modifiers);
        Assert.Equal(0x20u, g.VirtualKey);   // VK_SPACE
    }

    [Fact]
    public void Parses_AltShiftQ()
    {
        Assert.True(HotkeyGesture.TryParse("Alt+Shift+Q", out var g));
        Assert.Equal(Alt | Shift, g.Modifiers);
        Assert.Equal((uint)'Q', g.VirtualKey);
    }

    [Fact]
    public void Parses_FunctionKey()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+F12", out var g));
        Assert.Equal(Ctrl, g.Modifiers);
        Assert.Equal(0x7Bu, g.VirtualKey);   // VK_F12
    }

    [Fact]
    public void Parses_WindowsModifier_And_Digit()
    {
        Assert.True(HotkeyGesture.TryParse("Win+1", out var g));
        Assert.Equal(Win, g.Modifiers);
        Assert.Equal((uint)'1', g.VirtualKey);
    }

    [Theory]
    [InlineData("control+alt+space")]
    [InlineData("  Ctrl + Alt + Space  ")]
    public void Is_CaseInsensitive_And_TrimsWhitespace(string gesture)
    {
        Assert.True(HotkeyGesture.TryParse(gesture, out var g));
        Assert.Equal(Ctrl | Alt, g.Modifiers);
        Assert.Equal(0x20u, g.VirtualKey);
    }

    [Fact]
    public void Allows_MainKey_Without_Modifiers()
    {
        Assert.True(HotkeyGesture.TryParse("F9", out var g));
        Assert.Equal(0u, g.Modifiers);
        Assert.Equal(0x78u, g.VirtualKey);
    }

    [Theory]
    [InlineData("Meta+Space")]
    [InlineData("Super+Space")]
    [InlineData("Windows+Space")]
    public void Meta_Super_Windows_AllMapTo_Win(string gesture)
    {
        Assert.True(HotkeyGesture.TryParse(gesture, out var g));
        Assert.Equal(Win, g.Modifiers);
        Assert.Equal(0x20u, g.VirtualKey);
    }

    [Fact]
    public void DuplicateModifier_IsIdempotent()
    {
        // "Ctrl+Ctrl+A" OR's the same flag twice — harmless; assert it still parses to one Ctrl.
        Assert.True(HotkeyGesture.TryParse("Ctrl+Ctrl+A", out var g));
        Assert.Equal(Ctrl, g.Modifiers);
        Assert.Equal((uint)'A', g.VirtualKey);
    }

    [Theory]
    [InlineData("space", 0x20u)]
    [InlineData("ENTER", 0x0Du)]
    [InlineData("Return", 0x0Du)]
    [InlineData("del", 0x2Eu)]
    [InlineData("PgUp", 0x21u)]
    [InlineData("f9", 0x78u)]      // function-key letter is case-insensitive
    public void NamedKeys_AreCaseInsensitive(string token, uint vk)
    {
        Assert.True(HotkeyGesture.TryParse(token, out var g));
        Assert.Equal(vk, g.VirtualKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl")]              // modifiers only, no main key
    [InlineData("Ctrl+Alt")]         // modifiers only
    [InlineData("Win")]              // single modifier, no main key
    [InlineData("Ctrl+")]            // trailing empty token
    [InlineData("Ctrl++Space")]     // empty token in the middle
    [InlineData("+")]               // only a separator
    [InlineData("Ctrl+Bogus")]      // unknown key name
    [InlineData("Ctrl+A+B")]        // two main keys
    [InlineData("Ctrl+;")]          // punctuation key: not supported (documents current behaviour)
    [InlineData("Ctrl+.")]          // punctuation key: not supported
    [InlineData("F25")]             // out-of-range function key
    [InlineData("F0")]              // out-of-range function key
    [InlineData("Ctrl Alt Space")]  // spaces instead of '+' is a single unknown token
    public void Rejects_Invalid(string? gesture)
    {
        Assert.False(HotkeyGesture.TryParse(gesture, out var g));
        Assert.Equal(default, g);
    }
}

/// <summary>
/// Tests for <see cref="HotkeyGesture.Format"/> — the capture-control formatter that turns a
/// pressed WPF combo back into a gesture string. Its output must round-trip through
/// <see cref="HotkeyGesture.TryParse"/> to the same modifiers + virtual key.
/// </summary>
public class HotkeyGestureFormatTests
{
    private const uint Ctrl = HotkeyGesture.MOD_CONTROL;
    private const uint Alt = HotkeyGesture.MOD_ALT;
    private const uint Shift = HotkeyGesture.MOD_SHIFT;
    private const uint Win = HotkeyGesture.MOD_WIN;

    [Fact]
    public void Formats_Canonical_CtrlAltD()
    {
        Assert.Equal("Ctrl+Alt+D", HotkeyGesture.Format(ModifierKeys.Control | ModifierKeys.Alt, Key.D));
    }

    [Fact]
    public void ModifierOrder_IsCanonical_CtrlAltShiftWin()
    {
        var s = HotkeyGesture.Format(
            ModifierKeys.Windows | ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Control, Key.A);
        Assert.Equal("Ctrl+Alt+Shift+Win+A", s);
    }

    [Theory]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt, Key.D, Ctrl | Alt, (uint)'D')]
    [InlineData(ModifierKeys.Alt | ModifierKeys.Shift, Key.Q, Alt | Shift, (uint)'Q')]
    [InlineData(ModifierKeys.Control, Key.F12, Ctrl, 0x7Bu)]
    [InlineData(ModifierKeys.Windows, Key.D1, Win, (uint)'1')]
    [InlineData(ModifierKeys.Control, Key.NumPad5, Ctrl, (uint)'5')]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift, Key.Space, Ctrl | Shift, 0x20u)]
    [InlineData(ModifierKeys.Alt, Key.Return, Alt, 0x0Du)]
    [InlineData(ModifierKeys.Control, Key.Prior, Ctrl, 0x21u)]
    [InlineData(ModifierKeys.Control, Key.Next, Ctrl, 0x22u)]
    [InlineData(ModifierKeys.Control, Key.Delete, Ctrl, 0x2Eu)]
    [InlineData(ModifierKeys.None, Key.F9, 0u, 0x78u)]
    public void RoundTrips_Through_TryParse(ModifierKeys mods, Key key, uint expectMods, uint expectVk)
    {
        string? gesture = HotkeyGesture.Format(mods, key);
        Assert.NotNull(gesture);
        Assert.True(HotkeyGesture.TryParse(gesture, out var g));
        Assert.Equal(expectMods, g.Modifiers);
        Assert.Equal(expectVk, g.VirtualKey);
    }

    [Theory]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.RightCtrl)]
    [InlineData(Key.LeftAlt)]
    [InlineData(Key.LeftShift)]
    [InlineData(Key.LWin)]
    [InlineData(Key.System)]         // Alt held with no real key yet
    [InlineData(Key.OemQuestion)]    // punctuation: unmapped, keep waiting
    public void ReturnsNull_ForBareModifiers_And_UnmappableKeys(Key key)
    {
        Assert.Null(HotkeyGesture.Format(ModifierKeys.Control, key));
    }
}
