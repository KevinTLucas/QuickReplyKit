using System.Collections.Generic;
using System.Windows;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Tests;

public class WindowPlacementTests
{
    // A single 1920x1080 monitor at the origin, unless a test supplies its own set.
    private static readonly IReadOnlyList<Rect> OneScreen = new[] { new Rect(0, 0, 1920, 1080) };
    private static readonly Size Min = new(300, 200);
    private static readonly Size Default = new(400, 300);

    private static WindowBounds Saved(double x, double y, double w, double h) =>
        new() { X = x, Y = y, Width = w, Height = h };

    private static void AssertRect(WindowBounds b, double x, double y, double w, double h)
    {
        Assert.Equal(x, b.X);
        Assert.Equal(y, b.Y);
        Assert.Equal(w, b.Width);
        Assert.Equal(h, b.Height);
    }

    [Fact]
    public void NormalRect_KeptUnchanged()
    {
        var r = WindowPlacement.EnsureVisible(Saved(200, 150, 600, 400), OneScreen, Min, Default);
        AssertRect(r, 200, 150, 600, 400);
    }

    [Fact]
    public void FullyOffScreen_Recentered()
    {
        var r = WindowPlacement.EnsureVisible(Saved(5000, 5000, 600, 400), OneScreen, Min, Default);
        // Centred on the only screen at its (present) saved size.
        AssertRect(r, 660, 340, 600, 400);
    }

    [Fact]
    public void MonitorRemoved_MovedOntoRemainingArea()
    {
        // Saved on a second monitor that no longer exists; only screen 1 remains.
        var r = WindowPlacement.EnsureVisible(Saved(2200, 100, 600, 400), OneScreen, Min, Default);
        AssertRect(r, 660, 340, 600, 400);
    }

    [Fact]
    public void MonitorRemoved_MovedOntoNearestRemainingArea()
    {
        // Two remaining screens; saved centre is nearer the right-hand one.
        var screens = new[] { new Rect(0, 0, 1920, 1080), new Rect(1920, 0, 1920, 1080) };
        var r = WindowPlacement.EnsureVisible(Saved(3000, 2000, 600, 400), screens, Min, Default);
        // Centred on the right screen: left 1920 + (1920-600)/2 = 2580, top (1080-400)/2 = 340.
        AssertRect(r, 2580, 340, 600, 400);
    }

    [Fact]
    public void TitleAboveTop_NudgedDown()
    {
        var r = WindowPlacement.EnsureVisible(Saved(100, -30, 600, 400), OneScreen, Min, Default);
        AssertRect(r, 100, 0, 600, 400);
    }

    [Fact]
    public void OffRightEdge_NudgedLeft()
    {
        var r = WindowPlacement.EnsureVisible(Saved(1500, 100, 600, 400), OneScreen, Min, Default);
        // Right edge pulled to 1920: x = 1920 - 600 = 1320.
        AssertRect(r, 1320, 100, 600, 400);
    }

    [Fact]
    public void BelowMinimum_GrownToMinSize()
    {
        var r = WindowPlacement.EnsureVisible(Saved(100, 100, 50, 40), OneScreen, Min, Default);
        AssertRect(r, 100, 100, 300, 200);
    }

    [Fact]
    public void Oversized_ClampedToWorkArea()
    {
        var r = WindowPlacement.EnsureVisible(Saved(0, 0, 5000, 5000), OneScreen, Min, Default);
        AssertRect(r, 0, 0, 1920, 1080);
    }

    [Fact]
    public void EmptyWorkAreas_DefaultAtOrigin()
    {
        var r = WindowPlacement.EnsureVisible(Saved(200, 150, 600, 400), new List<Rect>(), Min, Default);
        AssertRect(r, 0, 0, 400, 300);
    }

    [Fact]
    public void OffScreen_NoSavedSize_UsesDefaultSize()
    {
        // Fresh config: position off-screen and no stored size -> default size, centred.
        var r = WindowPlacement.EnsureVisible(Saved(5000, 5000, 0, 0), OneScreen, Min, Default);
        AssertRect(r, 760, 390, 400, 300);
    }

    [Fact]
    public void IsReasonablyVisible_ReflectsOverlap()
    {
        Assert.True(WindowPlacement.IsReasonablyVisible(new Rect(200, 150, 600, 400), OneScreen));
        Assert.False(WindowPlacement.IsReasonablyVisible(new Rect(5000, 5000, 600, 400), OneScreen));
    }
}
