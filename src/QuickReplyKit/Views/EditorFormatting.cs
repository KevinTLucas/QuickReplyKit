using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace QuickReplyKit.Views;

internal static class EditorFormatting
{
    internal static void ToggleStrike(TextSelection selection)
    {
        var current = selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
        bool remove = current?.Any(d => d.Location == TextDecorationLocation.Strikethrough) == true;
        var next = new TextDecorationCollection(current?.Where(d =>
            d.Location != TextDecorationLocation.Strikethrough) ?? []);
        if (!remove) next.Add(TextDecorations.Strikethrough[0]);
        selection.ApplyPropertyValue(Inline.TextDecorationsProperty, next);
    }

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
