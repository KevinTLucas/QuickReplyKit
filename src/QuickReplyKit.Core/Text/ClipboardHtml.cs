using System.Text;
using System.Text.RegularExpressions;

namespace QuickReplyKit.Core.Text;

/// <summary>
/// Turns clipboard content into the stored HTML subset so "New from clipboard" can pre-populate
/// the editor. HTML on the clipboard arrives as Windows CF_HTML (a header plus a fragment); we
/// pull out the fragment, remove active/hidden blocks, and let <see cref="HtmlToFlowDocument"/>
/// normalize what remains into the subset. Plain text is wrapped into paragraphs (blank line = new &lt;p&gt;, single break
/// = &lt;br&gt;). Both are pure string transforms so they are unit-tested without a clipboard.
/// </summary>
public static class ClipboardHtml
{
    private const string StartMarker = "<!--StartFragment-->";
    private const string EndMarker = "<!--EndFragment-->";
    private static readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "head", "noscript", "iframe", "object", "embed", "svg", "math",
        "canvas", "template", "form", "select", "textarea",
    };
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param",
        "source", "track", "wbr",
    };

    /// <summary>Extract the HTML fragment from a CF_HTML payload; returns the input unchanged when
    /// it carries no CF_HTML header/markers.</summary>
    public static string ExtractFragment(string? cfHtml)
    {
        if (string.IsNullOrEmpty(cfHtml)) return "";

        int s = cfHtml.IndexOf(StartMarker, StringComparison.OrdinalIgnoreCase);
        int e = cfHtml.IndexOf(EndMarker, StringComparison.OrdinalIgnoreCase);
        if (s >= 0 && e > s)
        {
            int from = s + StartMarker.Length;
            return cfHtml[from..e].Trim();
        }

        // No fragment markers: strip a "Version:0.9\nStartHTML:…\n…" CF_HTML header if present.
        if (cfHtml.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
        {
            int lt = cfHtml.IndexOf('<');
            return lt >= 0 ? cfHtml[lt..].Trim() : "";
        }

        return cfHtml.Trim();
    }

    /// <summary>Normalize clipboard HTML into the supported stored subset. Active, hidden, and
    /// non-content elements are removed before the existing rich-text parser/serializer drops
    /// unsupported tags and attributes.</summary>
    public static string Sanitize(string? cfHtml)
    {
        string html = ExtractFragment(cfHtml);
        if (html.Length == 0) return "";
        try
        {
            string visible = VisibleMarkup(html);
            return FlowDocumentToHtml.Convert(HtmlToFlowDocument.Convert(visible));
        }
        catch { return ""; }
    }

    /// <summary>Wrap plain text as subset HTML: blank lines split paragraphs, single newlines
    /// become &lt;br&gt;. Text is HTML-escaped.</summary>
    public static string FromPlainText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var sb = new StringBuilder();
        foreach (string block in normalized.Split("\n\n"))
        {
            string trimmed = block.Trim('\n');
            if (trimmed.Length == 0) continue;

            sb.Append("<p>");
            string[] lines = trimmed.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append("<br>");
                Escape(sb, lines[i]);
            }
            sb.Append("</p>");
        }
        return sb.ToString();
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

    private static string VisibleMarkup(string html)
    {
        var output = new StringBuilder(html.Length);
        var skipped = new Stack<string>();
        int i = 0;
        while (i < html.Length)
        {
            if (html.AsSpan(i).StartsWith("<!--", StringComparison.Ordinal))
            {
                int commentEnd = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                i = commentEnd < 0 ? html.Length : commentEnd + 3;
                continue;
            }
            if (html[i] != '<')
            {
                int next = html.IndexOf('<', i);
                if (next < 0) next = html.Length;
                if (skipped.Count == 0) output.Append(html, i, next - i);
                i = next;
                continue;
            }

            int end = TagEnd(html, i + 1);
            if (end < 0)
            {
                if (skipped.Count == 0) Escape(output, html[i..]);
                break;
            }

            string tag = html[i..(end + 1)];
            string name = TagName(tag);
            bool closing = tag.Length > 1 && tag[1] == '/';
            bool selfClosing = tag.AsSpan(0, tag.Length - 1).TrimEnd().EndsWith("/", StringComparison.Ordinal);
            bool hasNoContent = selfClosing || VoidElements.Contains(name);

            if (skipped.Count > 0)
            {
                if (closing && name.Equals(skipped.Peek(), StringComparison.OrdinalIgnoreCase)) skipped.Pop();
                else if (!closing && !hasNoContent && Blocked.Contains(name)) skipped.Push(name);
            }
            else if (!closing && (Blocked.Contains(name) || Hidden(tag)))
            {
                if (!hasNoContent) skipped.Push(name);
            }
            else
            {
                output.Append(tag);
            }
            i = end + 1;
        }
        return output.ToString();
    }

    private static int TagEnd(string html, int from)
    {
        char quote = '\0';
        for (int i = from; i < html.Length; i++)
        {
            char c = html[i];
            if (quote == '\0' && (c == '\'' || c == '"')) quote = c;
            else if (c == quote) quote = '\0';
            else if (c == '>' && quote == '\0') return i;
        }
        return -1;
    }

    private static string TagName(string tag)
    {
        int i = 1;
        while (i < tag.Length && (tag[i] == '/' || char.IsWhiteSpace(tag[i]))) i++;
        int start = i;
        while (i < tag.Length && (char.IsLetterOrDigit(tag[i]) || tag[i] is '-' or ':')) i++;
        return tag[start..i];
    }

    private static bool Hidden(string tag) =>
        Regex.IsMatch(tag, @"\s(hidden|inert)(\s|=|/|>)", RegexOptions.IgnoreCase) ||
        Regex.IsMatch(tag, "\\saria-hidden\\s*=\\s*(['\"]?)true\\1", RegexOptions.IgnoreCase) ||
        Regex.IsMatch(tag, @"\b(display\s*:\s*none|visibility\s*:\s*hidden|opacity\s*:\s*0(?:\D|$))",
            RegexOptions.IgnoreCase);
}
