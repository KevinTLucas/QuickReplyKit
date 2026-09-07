using System.Diagnostics;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;

namespace QuickReplyKit.Core.Text;

/// <summary>
/// Renders the stored rich-text HTML subset (p, br, b/strong, i/em, u, ul/ol/li, a) into a
/// read-only WPF <see cref="FlowDocument"/> for preview. Bounded subset only, so a small
/// stack machine is enough — no HTML library. Malformed input never throws (best-effort:
/// keep whatever built). Hyperlinks open in the default browser on click.
/// </summary>
public static class HtmlToFlowDocument
{
    public static FlowDocument Convert(string? html)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
        };
        if (string.IsNullOrEmpty(html)) return doc;

        try { Build(doc, html); }
        catch { /* malformed input must never throw; keep what we have */ }
        return doc;
    }

    private static void Build(FlowDocument doc, string html)
    {
        Paragraph? para = null;
        var inlines = new Stack<InlineCollection>();
        var lists = new Stack<List>();
        var items = new Stack<ListItem>();

        BlockCollection BlockTarget() => items.Count > 0 ? items.Peek().Blocks : doc.Blocks;

        void ClosePara() { para = null; inlines.Clear(); }

        void EnsurePara()
        {
            if (para != null) return;
            para = new Paragraph();
            BlockTarget().Add(para);
            inlines.Clear();
            inlines.Push(para.Inlines);
        }

        void PushSpan(Span span)
        {
            EnsurePara();
            inlines.Peek().Add(span);
            inlines.Push(span.Inlines);
        }
        void PopSpan() { if (inlines.Count > 1) inlines.Pop(); }

        void AddText(string raw)
        {
            string collapsed = CollapseWhitespace(raw);
            bool wsOnly = collapsed.Length == 0 || collapsed == " ";
            if (wsOnly && para == null) return;      // ignore whitespace between block tags
            string text = WebUtility.HtmlDecode(collapsed);
            if (text.Length == 0) return;
            EnsurePara();
            inlines.Peek().Add(new Run(text));
        }

        void HandleTag(string tag)
        {
            bool closing = tag.Length > 1 && tag[1] == '/';
            string name = TagName(tag);
            switch (name)
            {
                case "p":
                    ClosePara();
                    if (!closing)
                    {
                        EnsurePara();
                        var al = AlignFromStyle(Attr(tag, "style"));
                        if (al != null) para!.TextAlignment = al.Value;
                    }
                    break;
                case "br":
                    EnsurePara();
                    inlines.Peek().Add(new LineBreak());
                    break;
                case "b":
                case "strong":
                    if (closing) PopSpan(); else PushSpan(new Bold());
                    break;
                case "i":
                case "em":
                    if (closing) PopSpan(); else PushSpan(new Italic());
                    break;
                case "u":
                    if (closing) PopSpan(); else PushSpan(new Underline());
                    break;
                case "s":
                case "strike":
                case "del":
                    if (closing) PopSpan();
                    else PushSpan(new Span { TextDecorations = TextDecorations.Strikethrough });
                    break;
                case "span":
                    if (closing) PopSpan();
                    else PushSpan(SpanFromStyle(Attr(tag, "style")));
                    break;
                case "a":
                    if (closing) { PopSpan(); break; }
                    var link = new Hyperlink();
                    string? href = Attr(tag, "href");
                    if (href != null && Uri.TryCreate(href, UriKind.Absolute, out var uri) && SafeLink(uri))
                    {
                        link.NavigateUri = uri;
                        link.RequestNavigate += OnNavigate;
                    }
                    PushSpan(link);
                    break;
                case "ul":
                case "ol":
                    if (closing) { if (lists.Count > 0) lists.Pop(); ClosePara(); break; }
                    ClosePara();
                    var list = new List
                    {
                        MarkerStyle = name == "ol" ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                    };
                    BlockTarget().Add(list);
                    lists.Push(list);
                    break;
                case "li":
                    if (closing) { ClosePara(); if (items.Count > 0) items.Pop(); break; }
                    if (lists.Count == 0) break;      // stray <li>
                    ClosePara();
                    var li = new ListItem();
                    lists.Peek().ListItems.Add(li);
                    items.Push(li);
                    EnsurePara();
                    break;
                // unknown tags: ignore, keep inner text
            }
        }

        int i = 0;
        while (i < html.Length)
        {
            if (html[i] == '<')
            {
                int end = ClipboardHtml.TagEnd(html, i + 1);
                if (end < 0) { AddText(html[i..]); break; }
                HandleTag(html[i..(end + 1)]);
                i = end + 1;
            }
            else
            {
                int next = html.IndexOf('<', i);
                if (next < 0) next = html.Length;
                AddText(html[i..next]);
                i = next;
            }
        }
    }

    private static void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { /* ignore navigation failures */ }
        e.Handled = true;
    }

    private static bool SafeLink(Uri uri) =>
        uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
        uri.Scheme.Equals(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase);

    // <span style="color:…; background-color:…; font-size:…px"> → a Span carrying those local
    // properties. Unknown/unparseable declarations are ignored (best effort, never throws).
    private static Span SpanFromStyle(string? style)
    {
        var span = new Span();
        if (string.IsNullOrEmpty(style)) return span;
        foreach (var decl in style.Split(';'))
        {
            int c = decl.IndexOf(':');
            if (c < 0) continue;
            string key = decl[..c].Trim().ToLowerInvariant();
            string val = decl[(c + 1)..].Trim();
            switch (key)
            {
                case "color": if (TryBrush(val, out var f)) span.Foreground = f; break;
                case "background-color": if (TryBrush(val, out var b)) span.Background = b; break;
                case "font-size": if (TryFontSize(val, out double sz)) span.FontSize = sz; break;
            }
        }
        return span;
    }

    private static TextAlignment? AlignFromStyle(string? style)
    {
        if (string.IsNullOrEmpty(style)) return null;
        foreach (var decl in style.Split(';'))
        {
            int c = decl.IndexOf(':');
            if (c < 0) continue;
            if (decl[..c].Trim().Equals("text-align", StringComparison.OrdinalIgnoreCase))
            {
                return decl[(c + 1)..].Trim().ToLowerInvariant() switch
                {
                    "center" => TextAlignment.Center,
                    "right" => TextAlignment.Right,
                    "justify" => TextAlignment.Justify,
                    "left" => TextAlignment.Left,
                    _ => null,
                };
            }
        }
        return null;
    }

    private static bool TryBrush(string css, out SolidColorBrush brush)
    {
        try
        {
            if (ColorConverter.ConvertFromString(css) is Color color)
            {
                brush = new SolidColorBrush(color);
                brush.Freeze();
                return true;
            }
        }
        catch { /* not a colour we understand: skip */ }
        brush = null!;
        return false;
    }

    private static bool TryFontSize(string css, out double size)
    {
        css = css.Trim();
        if (css.EndsWith("px", StringComparison.OrdinalIgnoreCase)) css = css[..^2].Trim();
        return double.TryParse(css, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out size) && size > 0;
    }

    private static string? Attr(string tag, string name) => ClipboardHtml.Attribute(tag, name);

    private static string CollapseWhitespace(string s)
    {
        if (s.Length == 0) return s;
        var sb = new StringBuilder(s.Length);
        bool prevWs = false;
        foreach (char ch in s)
        {
            if (char.IsWhiteSpace(ch)) { if (!prevWs) sb.Append(' '); prevWs = true; }
            else { sb.Append(ch); prevWs = false; }
        }
        return sb.ToString();
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
