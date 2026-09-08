using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace QuickReplyKit.Views;

internal static class EditorFormatting
{
    internal static void ToggleStrike(TextSelection selection)
    {
        var runs = RunsIn(selection).ToArray();
        if (runs.Length == 0)
        {
            var current = selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
            ApplyStrike(selection, current, HasStrike(current));
            return;
        }

        bool remove = runs.All(run => HasStrike(run.TextDecorations));
        foreach (var run in runs)
        {
            var start = selection.Start.CompareTo(run.ContentStart) > 0 ? selection.Start : run.ContentStart;
            var end = selection.End.CompareTo(run.ContentEnd) < 0 ? selection.End : run.ContentEnd;
            if (start.CompareTo(end) < 0)
                ApplyStrike(new TextRange(start, end), run.TextDecorations, remove);
        }
    }

    private static IEnumerable<Run> RunsIn(TextRange range)
    {
        var seen = new HashSet<Run>();
        for (var position = range.Start; position != null && position.CompareTo(range.End) < 0;
             position = position.GetNextContextPosition(LogicalDirection.Forward))
            if (position.Parent is Run run && seen.Add(run)) yield return run;
    }

    private static void ApplyStrike(TextRange range, TextDecorationCollection? current, bool remove)
    {
        var next = new TextDecorationCollection(current?.Where(d =>
            d.Location != TextDecorationLocation.Strikethrough) ?? []);
        if (!remove && !HasStrike(next)) next.Add(TextDecorations.Strikethrough[0]);
        range.ApplyPropertyValue(Inline.TextDecorationsProperty, next);
    }

    private static bool HasStrike(TextDecorationCollection? decorations) =>
        decorations?.Any(d => d.Location == TextDecorationLocation.Strikethrough) == true;

    internal static void Clear(TextSelection selection)
    {
        selection.ClearAllProperties();
        selection.ApplyPropertyValue(Paragraph.TextAlignmentProperty, TextAlignment.Left);
    }

    internal static void UseDefaultForeground(TextSelection selection, System.Windows.Media.Brush foreground) =>
        selection.ApplyPropertyValue(TextElement.ForegroundProperty, foreground);

    internal static void RemoveHighlight(TextSelection selection) =>
        selection.ApplyPropertyValue(TextElement.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
}
