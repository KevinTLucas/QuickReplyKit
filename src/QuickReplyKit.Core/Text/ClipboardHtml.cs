using System.Text;

namespace QuickReplyKit.Core.Text;

/// <summary>
/// Turns clipboard content into the stored HTML subset so "New from clipboard" can pre-populate
/// the editor. HTML on the clipboard arrives as Windows CF_HTML (a header plus a fragment); we
/// pull out the fragment and let <see cref="HtmlToFlowDocument"/> drop anything outside the subset
/// when it loads. Plain text is wrapped into paragraphs (blank line = new &lt;p&gt;, single break
/// = &lt;br&gt;). Both are pure string transforms so they are unit-tested without a clipboard.
/// </summary>
public static class ClipboardHtml
{
    private const string StartMarker = "<!--StartFragment-->";
    private const string EndMarker = "<!--EndFragment-->";

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
}
