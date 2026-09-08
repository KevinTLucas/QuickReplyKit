using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace QuickReplyKit.Core.Text;

/// <summary>
/// Serializes an editor <see cref="FlowDocument"/> back to the stored rich-text HTML subset:
/// the original b/i/u, p, br, ul/ol/li, a — plus the widened set: strikethrough (&lt;s&gt;),
/// text colour / highlight / font-size (inline <c>style</c> spans), and paragraph alignment
/// (<c>&lt;p style="text-align:…"&gt;</c>). Formatting is resolved at each text run because WPF
/// represents loaded markup and toolbar edits differently. The output is a stable round-trip with
/// <see cref="HtmlToFlowDocument"/>: convert -&gt; load -&gt; convert is idempotent for the subset.
/// </summary>
public static class FlowDocumentToHtml
{
    /// <summary>Font size treated as "normal" — a run at this size emits no font-size span.</summary>
    internal const double NormalFontSize = 14;

    public static string Convert(FlowDocument? doc)
    {
        var sb = new StringBuilder();
        if (doc != null)
        {
            var defaults = new FormatState(
                Size: NormalFontSize,
                Foreground: doc.Foreground as SolidColorBrush);
            WriteBlocks(sb, doc.Blocks, defaults);
        }
        return sb.ToString();
    }

    private static void WriteBlocks(StringBuilder sb, BlockCollection blocks, FormatState defaults)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph p:
                    string? align = AlignOf(p);
                    sb.Append(align == null ? "<p>" : $"<p style=\"text-align:{align}\">");
                    WriteInlines(sb, p.Inlines, defaults, defaults);
                    sb.Append("</p>");
                    break;
                case List list:
                    WriteList(sb, list, defaults);
                    break;
                // any other block type is outside the subset: drop it
            }
        }
    }

    private static void WriteList(StringBuilder sb, List list, FormatState defaults)
    {
        string tag = list.MarkerStyle == TextMarkerStyle.Decimal ? "ol" : "ul";
        sb.Append('<').Append(tag).Append('>');
        foreach (var li in list.ListItems)
        {
            sb.Append("<li>");
            WriteListItem(sb, li, defaults);
            sb.Append("</li>");
        }
        sb.Append("</").Append(tag).Append('>');
    }

    // Keep the compact <li>text</li> form for one unaligned paragraph. Paragraph tags are required
    // when an item has alignment or multiple paragraphs so save/reload preserves those boundaries.
    private static void WriteListItem(StringBuilder sb, ListItem li, FormatState defaults)
    {
        bool wrapParagraphs = li.Blocks.OfType<Paragraph>().Skip(1).Any() ||
            li.Blocks.OfType<Paragraph>().Any(p => AlignOf(p) != null);
        foreach (var block in li.Blocks)
        {
            switch (block)
            {
                case Paragraph p:
                    string? align = AlignOf(p);
                    if (wrapParagraphs)
                    {
                        sb.Append("<p");
                        if (align != null) sb.Append(" style=\"text-align:").Append(align).Append('"');
                        sb.Append('>');
                    }
                    WriteInlines(sb, p.Inlines, defaults, defaults);
                    if (wrapParagraphs) sb.Append("</p>");
                    break;
                case List nested:
                    WriteList(sb, nested, defaults);
                    break;
            }
        }
    }

    private static void WriteInlines(StringBuilder sb, InlineCollection inlines,
        FormatState state, FormatState defaults)
    {
        foreach (var inline in inlines) WriteInline(sb, inline, state, defaults);
    }

    private static void WriteInline(StringBuilder sb, Inline inline,
        FormatState state, FormatState defaults)
    {
        state = ApplyLocal(state, inline);
        switch (inline)
        {
            case Run run:
                OpenWrap(sb, state, defaults, out string closeRun);
                Escape(sb, run.Text);
                sb.Append(closeRun);
                break;
            case LineBreak:
                sb.Append("<br>");
                break;
            case Bold b:
                WriteInlines(sb, b.Inlines, state, defaults);
                break;
            case Italic it:
                WriteInlines(sb, it.Inlines, state, defaults);
                break;
            case Underline u:
                WriteInlines(sb, u.Inlines, state, defaults);
                break;
            case Hyperlink a:
                sb.Append("<a");
                if (a.NavigateUri != null)
                {
                    sb.Append(" href=\"");
                    EscapeAttr(sb, a.NavigateUri.ToString());
                    sb.Append('"');
                }
                sb.Append('>');
                WriteInlines(sb, a.Inlines, state, defaults);
                sb.Append("</a>");
                break;
            case Span span:
                WriteInlines(sb, span.Inlines, state, defaults);
                break;
            // anything else (InlineUIContainer, Figure, …) is outside the subset: drop it
        }
    }

    private static FormatState ApplyLocal(FormatState state, Inline inline)
    {
        state = inline switch
        {
            Bold => state with { Bold = true },
            Italic => state with { Italic = true },
            Underline => state with { Underline = true },
            _ => state,
        };
        if (inline.ReadLocalValue(TextElement.FontWeightProperty) is FontWeight weight)
            state = state with { Bold = weight == FontWeights.Bold };
        if (inline.ReadLocalValue(TextElement.FontStyleProperty) is FontStyle fontStyle)
            state = state with { Italic = fontStyle == FontStyles.Italic };
        if (inline.ReadLocalValue(Inline.TextDecorationsProperty) is TextDecorationCollection decorations)
            state = state with
            {
                Underline = decorations.Any(d => d.Location == TextDecorationLocation.Underline),
                Strike = decorations.Any(d => d.Location == TextDecorationLocation.Strikethrough),
            };
        if (inline.ReadLocalValue(TextElement.ForegroundProperty) is SolidColorBrush foreground)
            state = state with { Foreground = foreground };
        if (inline.ReadLocalValue(TextElement.BackgroundProperty) is SolidColorBrush background)
            state = state with { Background = background.Color.A == 0 ? null : background };
        if (inline.ReadLocalValue(TextElement.FontSizeProperty) is double size)
            state = state with { Size = size };
        return state;
    }

    private static void OpenWrap(StringBuilder sb, FormatState state, FormatState defaults, out string close)
    {
        var closer = new StringBuilder();
        if (state.Bold)
        { sb.Append("<b>"); closer.Insert(0, "</b>"); }
        if (state.Italic)
        { sb.Append("<i>"); closer.Insert(0, "</i>"); }
        if (state.Underline)
        { sb.Append("<u>"); closer.Insert(0, "</u>"); }
        if (state.Strike)
        { sb.Append("<s>"); closer.Insert(0, "</s>"); }
        string? style = StyleOf(state, defaults);
        if (style != null) { sb.Append("<span style=\"").Append(style).Append("\">"); closer.Insert(0, "</span>"); }
        close = closer.ToString();
    }

    private static string? StyleOf(FormatState state, FormatState defaults)
    {
        var parts = new List<string>(3);
        if (state.Foreground != null && !SameColor(state.Foreground, defaults.Foreground))
            parts.Add("color:" + Hex(state.Foreground.Color));
        if (state.Background != null)
            parts.Add("background-color:" + Hex(state.Background.Color));
        if (Math.Abs(state.Size - defaults.Size) > 0.01)
            parts.Add("font-size:" + state.Size.ToString("0.###", CultureInfo.InvariantCulture) + "px");
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    private static bool SameColor(SolidColorBrush left, SolidColorBrush? right) =>
        right != null && left.Color == right.Color;

    private static string? AlignOf(Paragraph p) =>
        p.ReadLocalValue(Block.TextAlignmentProperty) is TextAlignment a
            ? a switch
            {
                TextAlignment.Center => "center",
                TextAlignment.Right => "right",
                TextAlignment.Justify => "justify",
                _ => null,   // Left is the default: emit nothing
            }
            : null;

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private readonly record struct FormatState(
        bool Bold = false,
        bool Italic = false,
        bool Underline = false,
        bool Strike = false,
        double Size = NormalFontSize,
        SolidColorBrush? Foreground = null,
        SolidColorBrush? Background = null);

    private static void Escape(StringBuilder sb, string text)
    {
        foreach (char c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default: sb.Append(c); break;
            }
        }
    }

    private static void EscapeAttr(StringBuilder sb, string text)
    {
        foreach (char c in text)
        {
            if (c == '&') sb.Append("&amp;");
            else if (c == '"') sb.Append("&quot;");
            else sb.Append(c);
        }
    }
}
