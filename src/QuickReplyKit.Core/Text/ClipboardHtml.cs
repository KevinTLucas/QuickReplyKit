using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;

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
    public const int MaxInputChars = 250_000;
    internal const int MaxTags = 4_096;
    internal const int MaxDepth = 256;
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

        if (TryOffsetFragment(cfHtml, out string fragment)) return fragment.Trim();

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
        if (cfHtml?.Length > MaxInputChars) return "";
        string html = ExtractFragment(cfHtml);
        if (html.Length == 0) return "";
        try
        {
            var hidden = HiddenSelectors(html);
            string visible = VisibleMarkup(html, hidden.Classes, hidden.Ids);
            return FlowDocumentToHtml.Convert(HtmlToFlowDocument.Convert(visible));
        }
        catch { return ""; }
    }

    /// <summary>Normalize clipboard RTF through WPF's native RTF reader, preserving the richer
    /// formatting Word places on the clipboard before narrowing it to the stored HTML subset.</summary>
    public static string FromRtf(string? rtf)
    {
        if (string.IsNullOrEmpty(rtf) || rtf.Length > MaxInputChars) return "";
        try
        {
            var document = new FlowDocument();
            using var stream = new MemoryStream(Encoding.Latin1.GetBytes(rtf));
            new TextRange(document.ContentStart, document.ContentEnd).Load(stream, DataFormats.Rtf);
            return FlowDocumentToHtml.Convert(document);
        }
        catch { return ""; }
    }

    /// <summary>Wrap plain text as subset HTML: blank lines split paragraphs, single newlines
    /// become &lt;br&gt;. Text is HTML-escaped.</summary>
    public static string FromPlainText(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxInputChars) return "";

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

    private static string VisibleMarkup(string html, HashSet<string> hiddenClasses, HashSet<string> hiddenIds)
    {
        var output = new StringBuilder(html.Length);
        var skipped = new Stack<string>();
        var open = new Stack<string>();
        int tags = 0;
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
                // An unterminated tag has no trustworthy visible-content boundary.
                break;
            }

            string tag = html[i..(end + 1)];
            string name = TagName(tag);
            bool closing = tag.Length > 1 && tag[1] == '/';
            bool hasNoContent = VoidElements.Contains(name);
            bool selfClosing = tag.AsSpan(0, tag.Length - 1).TrimEnd().EndsWith("/", StringComparison.Ordinal);
            if (++tags > MaxTags) return "";
            if (closing && open.TryPeek(out string? parent) && name.Equals(parent, StringComparison.OrdinalIgnoreCase))
                open.Pop();
            else if (!closing && !hasNoContent && !selfClosing)
            {
                open.Push(name);
                if (open.Count > MaxDepth) return "";
            }

            if (skipped.Count > 0)
            {
                if (closing && name.Equals(skipped.Peek(), StringComparison.OrdinalIgnoreCase)) skipped.Pop();
                else if (!closing && !hasNoContent
                    && name.Equals(skipped.Peek(), StringComparison.OrdinalIgnoreCase)) skipped.Push(name);
            }
            else if (!closing && (Blocked.Contains(name) || Hidden(tag, hiddenClasses, hiddenIds)))
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

    internal static int TagEnd(string html, int from)
    {
        char quote = '\0';
        bool awaitingValue = false;
        for (int i = from; i < html.Length; i++)
        {
            char c = html[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                continue;
            }

            if (c == '>') return i;
            if (awaitingValue)
            {
                if (char.IsWhiteSpace(c)) continue;
                if (c is '\'' or '"') quote = c;
                awaitingValue = false;
            }
            else if (c == '=') awaitingValue = true;
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

    internal static string? Attribute(string tag, string wanted)
    {
        int i = 1;
        while (i < tag.Length && (tag[i] == '/' || char.IsWhiteSpace(tag[i]))) i++;
        while (i < tag.Length && !char.IsWhiteSpace(tag[i]) && tag[i] is not '>' and not '/') i++;
        while (i < tag.Length)
        {
            while (i < tag.Length && (char.IsWhiteSpace(tag[i]) || tag[i] == '/')) i++;
            if (i >= tag.Length || tag[i] == '>') break;
            int nameStart = i;
            while (i < tag.Length && !char.IsWhiteSpace(tag[i]) && tag[i] is not '=' and not '>' and not '/') i++;
            string name = tag[nameStart..i];
            while (i < tag.Length && char.IsWhiteSpace(tag[i])) i++;
            string value = "";
            if (i < tag.Length && tag[i] == '=')
            {
                i++;
                while (i < tag.Length && char.IsWhiteSpace(tag[i])) i++;
                if (i < tag.Length && tag[i] is '\'' or '"')
                {
                    char quote = tag[i++];
                    int start = i;
                    while (i < tag.Length && tag[i] != quote) i++;
                    value = tag[start..i];
                    if (i < tag.Length) i++;
                }
                else
                {
                    int start = i;
                    while (i < tag.Length && !char.IsWhiteSpace(tag[i]) && tag[i] != '>') i++;
                    value = tag[start..i];
                }
            }
            if (name.Equals(wanted, StringComparison.OrdinalIgnoreCase)) return WebUtility.HtmlDecode(value);
        }
        return null;
    }

    private static bool Hidden(string tag, HashSet<string> hiddenClasses, HashSet<string> hiddenIds)
    {
        if (Attribute(tag, "hidden") is not null || Attribute(tag, "inert") is not null
            || Attribute(tag, "aria-hidden")?.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) == true)
            return true;
        string? classes = Attribute(tag, "class");
        if (classes?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Any(hiddenClasses.Contains) == true)
            return true;
        if (Attribute(tag, "id") is { } id && hiddenIds.Contains(id)) return true;
        return HiddenStyle(Attribute(tag, "style"));
    }

    private static bool HiddenStyle(string? style)
    {
        if (string.IsNullOrEmpty(style)) return false;
        style = Regex.Replace(style, @"/\*.*?\*/", "", RegexOptions.Singleline);
        foreach (string declaration in style.Split(';'))
        {
            int colon = declaration.IndexOf(':');
            if (colon < 0) continue;
            string key = CssUnescape(declaration[..colon].Trim());
            string value = Regex.Replace(CssUnescape(declaration[(colon + 1)..]),
                @"\s*!\s*important\s*$", "", RegexOptions.IgnoreCase).Trim();
            if (key.Equals("display", StringComparison.OrdinalIgnoreCase)
                    && value.Equals("none", StringComparison.OrdinalIgnoreCase)
                || key.Equals("visibility", StringComparison.OrdinalIgnoreCase)
                    && (value.Equals("hidden", StringComparison.OrdinalIgnoreCase)
                        || value.Equals("collapse", StringComparison.OrdinalIgnoreCase))
                || key.Equals("mso-hide", StringComparison.OrdinalIgnoreCase)
                    && value.Equals("all", StringComparison.OrdinalIgnoreCase)
                || key.Equals("opacity", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(value.TrimEnd('%'), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double opacity)
                    && opacity == 0)
                return true;
        }
        return false;
    }

    private static (HashSet<string> Classes, HashSet<string> Ids) HiddenSelectors(string html)
    {
        var classes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match block in Regex.Matches(html, @"<style\b[^>]*>(.*?)</style\s*>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        foreach (Match rule in Regex.Matches(
                     Regex.Replace(block.Groups[1].Value, @"/\*.*?\*/", "", RegexOptions.Singleline),
                     @"([^{}]+)\{([^{}]*)\}",
                     RegexOptions.Singleline))
        {
            if (!HiddenStyle(rule.Groups[2].Value)) continue;
            string selector = CssUnescape(rule.Groups[1].Value);
            foreach (Match cls in Regex.Matches(selector, @"\.([-A-Za-z_][\w-]*)"))
                classes.Add(cls.Groups[1].Value);
            foreach (Match id in Regex.Matches(selector, @"#([-A-Za-z_][\w-]*)"))
                ids.Add(id.Groups[1].Value);
        }
        return (classes, ids);
    }

    private static string CssUnescape(string value)
    {
        if (!value.Contains('\\')) return value;
        var result = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || ++i >= value.Length) { if (i < value.Length) result.Append(value[i]); continue; }
            int start = i;
            while (i < value.Length && i - start < 6 && Uri.IsHexDigit(value[i])) i++;
            if (i > start)
            {
                if (int.TryParse(value.AsSpan(start, i - start), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out int code) && code is > 0 and <= 0x10FFFF
                    && code is not (>= 0xD800 and <= 0xDFFF))
                    result.Append(char.ConvertFromUtf32(code));
                if (i < value.Length && char.IsWhiteSpace(value[i])) { }
                else i--;
            }
            else result.Append(value[i]);
        }
        return result.ToString();
    }

    private static bool TryOffsetFragment(string html, out string fragment)
    {
        fragment = "";
        if (!html.StartsWith("Version:", StringComparison.OrdinalIgnoreCase)
            || !HeaderOffset(html, "StartFragment", out int start)
            || !HeaderOffset(html, "EndFragment", out int end)) return false;
        byte[] bytes = Encoding.UTF8.GetBytes(html);
        if (start < 0 || end <= start || end > bytes.Length) return false;
        fragment = Encoding.UTF8.GetString(bytes, start, end - start);
        return true;
    }

    private static bool HeaderOffset(string html, string name, out int value)
    {
        value = 0;
        int start = html.IndexOf(name + ":", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return false;
        start += name.Length + 1;
        while (start < html.Length && char.IsWhiteSpace(html[start])) start++;
        int end = start;
        while (end < html.Length && char.IsDigit(html[end])) end++;
        return end > start && int.TryParse(html.AsSpan(start, end - start), out value);
    }
}
