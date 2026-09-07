using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Interop;

/// <summary>
/// Pure, OS-free analysis of the app's hotkey assignments: normalise gesture strings to an
/// order/case-independent key, detect duplicates (two binds on the same combo, or a bind equal to
/// the launcher hotkey), and answer "what is this combo bound to?" for the manager's search-by-key.
/// Registration with Windows lives elsewhere — this is only the logic, so it's unit-testable.
/// </summary>
public static class HotkeyConflicts
{
    public enum TargetKind { Free, Launcher, Response }

    /// <summary>The result of a search-by-key lookup: what (if anything) owns a combo.</summary>
    public readonly record struct Lookup(TargetKind Kind, string? RelativePath);

    /// <summary>
    /// Canonical key for a gesture string, independent of token order/casing (e.g. "Alt+Ctrl+d"
    /// and "Ctrl+Alt+D" map equal), or null when the gesture can't be parsed.
    /// </summary>
    public static HotkeyGesture? KeyOf(string? gesture) =>
        HotkeyGesture.TryParse(gesture, out var g) ? g : null;

    /// <summary>
    /// The set of canonical keys assigned more than once across the whole app (launcher + binds).
    /// An entry whose key is in this set collides with another QuickReplyKit binding.
    /// </summary>
    public static IReadOnlySet<HotkeyGesture> Duplicates(string launcherGesture, IEnumerable<ResponseHotkey> binds)
    {
        var seen = new HashSet<HotkeyGesture>();
        var dupes = new HashSet<HotkeyGesture>();

        void Note(string? g)
        {
            if (KeyOf(g) is { } key && !seen.Add(key)) dupes.Add(key);
        }

        Note(launcherGesture);
        foreach (var b in binds) Note(b.Gesture);
        return dupes;
    }

    /// <summary>
    /// What owns <paramref name="gesture"/>: the launcher, a specific response, or nothing (free).
    /// The launcher wins ties; among response binds the first match wins. An unparseable gesture is
    /// treated as free (it can never be registered anyway).
    /// </summary>
    public static Lookup Resolve(string? gesture, string launcherGesture, IEnumerable<ResponseHotkey> binds)
    {
        if (KeyOf(gesture) is not { } key) return new Lookup(TargetKind.Free, null);
        if (KeyOf(launcherGesture) == key) return new Lookup(TargetKind.Launcher, null);
        foreach (var b in binds)
            if (KeyOf(b.Gesture) == key) return new Lookup(TargetKind.Response, b.RelativePath);
        return new Lookup(TargetKind.Free, null);
    }
}
