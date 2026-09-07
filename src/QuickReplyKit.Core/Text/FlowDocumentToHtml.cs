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
/// (<c>&lt;p style="text-align:…"&gt;</c>). Only <b>locally set</b> properties are emitted, so
/// stored HTML stays clean and diffable and the output is a stable round-trip with
/// <see cref="HtmlToFlowDocument"/>: convert -&gt; load -&gt; convert is idempotent for the subset.
/// </summary>
public static class FlowDocumentToHtml
{
    /// <summary>Font size treated as "normal" — a run at this size emits no font-size span.</summary>
    internal const double NormalFontSize = 14;

    public static string Convert(FlowDocument? doc)
    {
        var sb = new StringBuilder();
        if (doc != null) WriteBlocks(sb, doc.Blocks);
        return sb.ToString();
    }

    private static void WriteBlocks(StringBuilder sb, BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph p:
                    string? align = AlignOf(p);
                    sb.Append(align == null ? "<p>" : $"<p style=\"text-align:{align}\">");
                    WriteInlines(sb, p.Inlines);
                    sb.Append("</p>");
                    break;
                case List list:
                    WriteList(sb, list);
                    break;
                // any other block type is outside the subset: drop it
            }
        }
    }

    private static void WriteList(StringBuilder sb, List list)
    {
        string tag = list.MarkerStyle == TextMarkerStyle.Decimal ? "ol" : "ul";
        sb.Append('<').Append(tag).Append('>');
        foreach (var li in list.ListItems)
        {
            sb.Append("<li>");
            WriteListItem(sb, li);
            sb.Append("</li>");
        }
        sb.Append("</").Append(tag).Append('>');
    }

    // A ListItem's text lives in Paragraph blocks; emit their inlines directly (no <p>) so the
    // markup matches what HtmlToFlowDocument reads back (<li>text<ul>…</ul></li>). Nested lists
    // are emitted inline. Multiple paragraphs in one item (rare) are separated with <br>.
    private static void WriteListItem(StringBuilder sb, ListItem li)
    {
        bool firstPara = true;
        foreach (var block in li.Blocks)
        {
            switch (block)
            {
                case Paragraph p:
                    if (!firstPara) sb.Append("<br>");
                    WriteInlines(sb, p.Inlines);
                    firstPara = false;
                    break;
                case List nested:
                    WriteList(sb, nested);
                    break;
            }
        }
    }

    private static void WriteInlines(StringBuilder sb, InlineCollection inlines)
    {
        foreach (var inline in inlines) WriteInline(sb, inline);
    }

    private static void WriteInline(StringBuilder sb, Inline inline)
    {
        switch (inline)
        {
            case Run run:
                OpenWrap(sb, run, out string closeRun);
                Escape(sb, run.Text);
                sb.Append(closeRun);
                break;
            case LineBreak:
                sb.Append("<br>");
                break;
            case Bold b:
                WrapElement(sb, b, "b", b.Inlines);
                break;
            case Italic it:
                WrapElement(sb, it, "i", it.Inlines);
                break;
            case Underline u:
                WrapElement(sb, u, "u", u.Inlines);
                break;
            case Hyperlink a:
                OpenWrap(sb, a, out string closeA);
                sb.Append("<a");
                if (a.NavigateUri != null)
                {
                    sb.Append(" href=\"");
                    EscapeAttr(sb, a.NavigateUri.ToString());
                    sb.Append('"');
                }
                sb.Append('>');
                WriteInlines(sb, a.Inlines);
                sb.Append("</a>");
                sb.Append(closeA);
                break;
            case Span span:   // generic span (colour / highlight / size / strike): keep its styling
                OpenWrap(sb, span, out string closeS);
                WriteInlines(sb, span.Inlines);
                sb.Append(closeS);
                break;
            // anything else (InlineUIContainer, Figure, …) is outside the subset: drop it
        }
    }

    // Emit the <s> (strikethrough) and <span style="…"> (colour/highlight/size) wrappers that a
    // given inline's locally-set properties call for, and hand back the matching close string.
    private static void OpenWrap(StringBuilder sb, Inline el, out string close)
    {
        var closer = new StringBuilder();
        if (HasStrike(el)) { sb.Append("<s>"); closer.Insert(0, "</s>"); }
        string? style = StyleOf(el);
        if (style != null) { sb.Append("<span style=\"").Append(style).Append("\">"); closer.Insert(0, "</span>"); }
        close = closer.ToString();
    }

    private static void WrapElement(StringBuilder sb, Inline el, string tag, InlineCollection children)
    {
        OpenWrap(sb, el, out string close);
        sb.Append('<').Append(tag).Append('>');
        WriteInlines(sb, children);
        sb.Append("</").Append(tag).Append('>');
        sb.Append(close);
    }

    private static bool HasStrike(Inline el) =>
        el.ReadLocalValue(Inline.TextDecorationsProperty) is TextDecorationCollection td &&
        td.Any(d => d.Location == TextDecorationLocation.Strikethrough);

    private static string? StyleOf(Inline el)
    {
        var parts = new List<string>(3);
        if (el.ReadLocalValue(TextElement.ForegroundProperty) is SolidColorBrush fg)
            parts.Add("color:" + Hex(fg.Color));
        if (el.ReadLocalValue(TextElement.BackgroundProperty) is SolidColorBrush bg && bg.Color.A != 0)
            parts.Add("background-color:" + Hex(bg.Color));
        if (el.ReadLocalValue(TextElement.FontSizeProperty) is double sz && Math.Abs(sz - NormalFontSize) > 0.01)
            parts.Add("font-size:" + sz.ToString("0.###", CultureInfo.InvariantCulture) + "px");
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

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
