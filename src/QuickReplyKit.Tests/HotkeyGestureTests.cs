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
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl")]              // modifiers only, no main key
    [InlineData("Ctrl+Alt")]         // modifiers only
    [InlineData("Ctrl+")]            // trailing empty token
    [InlineData("Ctrl++Space")]     // empty token in the middle
    [InlineData("Ctrl+Bogus")]      // unknown key name
    [InlineData("Ctrl+A+B")]        // two main keys
    [InlineData("F25")]             // out-of-range function key
    [InlineData("F0")]              // out-of-range function key
    public void Rejects_Invalid(string? gesture)
    {
        Assert.False(HotkeyGesture.TryParse(gesture, out var g));
        Assert.Equal(default, g);
    }
}
