using System.Text;
using System.Windows;
using System.Windows.Documents;

namespace QuickReplyKit.Core.Text;

/// <summary>
/// Serializes an editor <see cref="FlowDocument"/> back to the stored rich-text HTML subset
/// (p, br, b, i, u, ul/ol/li, a) — nothing else. Colors, fonts and sizes are dropped on
/// purpose so stored HTML stays clean, portable and diffable. The output is a stable
/// round-trip with <see cref="HtmlToFlowDocument"/>: convert -> load -> convert is idempotent
/// for the subset.
/// </summary>
public static class FlowDocumentToHtml
{
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
                    sb.Append("<p>");
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
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    Escape(sb, run.Text);
                    break;
                case LineBreak:
                    sb.Append("<br>");
                    break;
                case Bold b:
                    sb.Append("<b>"); WriteInlines(sb, b.Inlines); sb.Append("</b>");
                    break;
                case Italic it:
                    sb.Append("<i>"); WriteInlines(sb, it.Inlines); sb.Append("</i>");
                    break;
                case Underline u:
                    sb.Append("<u>"); WriteInlines(sb, u.Inlines); sb.Append("</u>");
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
                    WriteInlines(sb, a.Inlines);
                    sb.Append("</a>");
                    break;
                case Span span:   // generic span (e.g. plain Span): unwrap, keep children
                    WriteInlines(sb, span.Inlines);
                    break;
                // anything else (InlineUIContainer, Figure, …) is outside the subset: drop it
            }
        }
    }

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

    // Only the quote needs escaping to keep the attribute well-formed. '&' is left as-is so the
    // href round-trips verbatim through HtmlToFlowDocument (which does not entity-decode hrefs).
    private static void EscapeAttr(StringBuilder sb, string text)
    {
        foreach (char c in text)
        {
            if (c == '"') sb.Append("&quot;");
            else sb.Append(c);
        }
    }
}
