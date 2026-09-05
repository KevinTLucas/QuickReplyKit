using System.Net;
using System.Text;

namespace QuickReplyKit.Core.Text;

/// <summary>
/// Converts the stored rich-text HTML subset (b/i/u, p, br, ul/ol/li, a) into readable
/// plain text. Bounded subset only — a tiny tokenizer is enough, no HTML library needed.
/// </summary>
public static class HtmlText
{
    private sealed class ListState { public bool Ordered; public int Counter; }

    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";

        var sb = new StringBuilder(html.Length);
        var lists = new Stack<ListState>();
        int i = 0;
        while (i < html.Length)
        {
            char c = html[i];
            if (c == '<')
            {
                int end = html.IndexOf('>', i);
                if (end < 0) { AppendText(sb, html[i..]); break; }
                string tag = html[i..(end + 1)];
                HandleTag(sb, tag, lists);
                i = end + 1;
            }
            else
            {
                int next = html.IndexOf('<', i);
                if (next < 0) next = html.Length;
                AppendText(sb, html[i..next]);
                i = next;
            }
        }
        return sb.ToString().Trim();
    }

    private static void HandleTag(StringBuilder sb, string tag, Stack<ListState> lists)
    {
        bool closing = tag.Length > 1 && tag[1] == '/';
        string name = TagName(tag);
        switch (name)
        {
            case "br":
                sb.Append('\n');
                break;
            case "p":
                if (closing) sb.Append('\n');
                else EnsureLineStart(sb);
                break;
            case "ul":
                if (closing) { if (lists.Count > 0) lists.Pop(); }
                else lists.Push(new ListState { Ordered = false });
                break;
            case "ol":
                if (closing) { if (lists.Count > 0) lists.Pop(); }
                else lists.Push(new ListState { Ordered = true, Counter = 0 });
                break;
            case "li":
                // Close to line start (not an unconditional '\n'): a nested list closing
                // just before the outer </li> already left us on a fresh line, so an
                // unconditional append would inject a spurious blank line between items.
                if (closing) EnsureLineStart(sb);
                else
                {
                    EnsureLineStart(sb);
                    if (lists.Count > 0 && lists.Peek().Ordered)
                    {
                        var st = lists.Peek();
                        st.Counter++;
                        sb.Append(st.Counter).Append(". ");
                    }
                    else sb.Append("• "); // bullet
                }
                break;
            // b, i, u, a and anything else: strip, keep inner text.
        }
    }

    private static void AppendText(StringBuilder sb, string raw)
    {
        if (raw.Length == 0) return;
        sb.Append(WebUtility.HtmlDecode(raw));
    }

    private static void EnsureLineStart(StringBuilder sb)
    {
        if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n');
    }

    private static string TagName(string tag)
    {
        int i = 1;
        if (i < tag.Length && tag[i] == '/') i++;
        int start = i;
        while (i < tag.Length && char.IsLetterOrDigit(tag[i])) i++;
        return tag[start..i].ToLowerInvariant();
    }
}
