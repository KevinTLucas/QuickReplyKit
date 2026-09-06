using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using QuickReplyKit.ViewModels;

namespace QuickReplyKit.Views;

/// <summary>
/// Attached property that renders a list of <see cref="NameSegment"/>s into a <see cref="TextBlock"/>'s
/// inline runs, bolding + accent-coloring the matched runs. The accent brush is bound via a resource
/// reference so it tracks the active (light/dark) theme.
/// </summary>
public static class HighlightText
{
    public static readonly DependencyProperty SegmentsProperty =
        DependencyProperty.RegisterAttached(
            "Segments", typeof(IEnumerable<NameSegment>), typeof(HighlightText),
            new PropertyMetadata(null, OnSegmentsChanged));

    public static void SetSegments(DependencyObject o, IEnumerable<NameSegment>? value) =>
        o.SetValue(SegmentsProperty, value);

    public static IEnumerable<NameSegment>? GetSegments(DependencyObject o) =>
        (IEnumerable<NameSegment>?)o.GetValue(SegmentsProperty);

    private static void OnSegmentsChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not TextBlock tb) return;
        tb.Inlines.Clear();
        if (e.NewValue is not IEnumerable<NameSegment> segments) return;

        foreach (var seg in segments)
        {
            var run = new Run(seg.Text);
            if (seg.Hit)
            {
                run.FontWeight = FontWeights.Bold;
                run.SetResourceReference(TextElement.ForegroundProperty, "Accent");
            }
            tb.Inlines.Add(run);
        }
    }
}
