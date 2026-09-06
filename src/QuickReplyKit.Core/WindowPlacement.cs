using System.Windows;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Core;

/// <summary>
/// Pure window-placement geometry that keeps the launcher on a visible monitor.
/// Monitors are supplied as work-area rectangles (the WPF app feeds it
/// <c>System.Windows.Forms.Screen.AllScreens</c> working areas on window show), so this stays
/// Win32-free and unit-testable. It reasons about geometry only; <see cref="WindowBounds.MonitorDeviceName"/>
/// is left to the caller.
/// </summary>
public static class WindowPlacement
{
    /// <summary>Minimum overlap (px, each axis) with a work area for the window to count as grabbable.</summary>
    private const double MinVisible = 40;

    /// <summary>
    /// Returns a placement guaranteed to sit inside one of <paramref name="workAreas"/>.
    /// Size is clamped to at least <paramref name="minSize"/> and at most the chosen work area.
    /// If <paramref name="saved"/> still overlaps a work area enough to grab, it is kept and nudged
    /// fully inside that area; otherwise it is re-centred on the work area nearest its centre — using
    /// the saved size (clamped) when present, else <paramref name="defaultSize"/>. Empty
    /// <paramref name="workAreas"/> yields <paramref name="defaultSize"/> at the origin.
    /// </summary>
    public static WindowBounds EnsureVisible(
        WindowBounds saved,
        IReadOnlyList<Rect> workAreas,
        Size minSize,
        Size defaultSize)
    {
        if (workAreas is null || workAreas.Count == 0)
            return At(0, 0, defaultSize.Width, defaultSize.Height);

        // Grow the saved size to the minimum; the final clamp to the chosen area happens below.
        double w = Math.Max(saved.Width, minSize.Width);
        double h = Math.Max(saved.Height, minSize.Height);
        var window = new Rect(saved.X, saved.Y, w, h);

        if (IsReasonablyVisible(window, workAreas))
        {
            // Keep it: settle onto the work area it overlaps most, clamp size, nudge fully inside.
            Rect area = MaxOverlap(window, workAreas);
            w = Math.Min(w, area.Width);
            h = Math.Min(h, area.Height);
            return At(Clamp(window.X, area.Left, area.Right - w),
                      Clamp(window.Y, area.Top, area.Bottom - h), w, h);
        }

        // Off-screen: re-centre on the work area nearest the saved centre.
        Rect target = Nearest(workAreas, Center(window));
        bool hasSize = saved.Width > 0 && saved.Height > 0;
        w = Math.Min(hasSize ? w : defaultSize.Width, target.Width);
        h = Math.Min(hasSize ? h : defaultSize.Height, target.Height);
        return At(target.Left + (target.Width - w) / 2,
                  target.Top + (target.Height - h) / 2, w, h);
    }

    /// <summary>True if <paramref name="window"/> overlaps any work area enough to see and grab.</summary>
    public static bool IsReasonablyVisible(Rect window, IReadOnlyList<Rect> workAreas)
    {
        if (workAreas is null) return false;
        foreach (var a in workAreas)
        {
            Rect i = Rect.Intersect(window, a);
            if (!i.IsEmpty && i.Width >= MinVisible && i.Height >= MinVisible) return true;
        }
        return false;
    }

    private static Rect MaxOverlap(Rect window, IReadOnlyList<Rect> workAreas)
    {
        Rect best = workAreas[0];
        double bestArea = OverlapArea(window, best);
        for (int i = 1; i < workAreas.Count; i++)
        {
            double a = OverlapArea(window, workAreas[i]);
            if (a > bestArea) { bestArea = a; best = workAreas[i]; }
        }
        return best;
    }

    private static Rect Nearest(IReadOnlyList<Rect> workAreas, Point c)
    {
        Rect best = workAreas[0];
        double bestD = DistSq(Center(best), c);
        for (int i = 1; i < workAreas.Count; i++)
        {
            double d = DistSq(Center(workAreas[i]), c);
            if (d < bestD) { bestD = d; best = workAreas[i]; }
        }
        return best;
    }

    private static double OverlapArea(Rect a, Rect b)
    {
        Rect i = Rect.Intersect(a, b);
        return i.IsEmpty ? 0 : i.Width * i.Height;
    }

    private static Point Center(Rect r) => new(r.Left + r.Width / 2, r.Top + r.Height / 2);

    private static double DistSq(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static double Clamp(double v, double lo, double hi) => Math.Min(Math.Max(v, lo), hi);

    private static WindowBounds At(double x, double y, double w, double h) =>
        new() { X = x, Y = y, Width = w, Height = h };
}
