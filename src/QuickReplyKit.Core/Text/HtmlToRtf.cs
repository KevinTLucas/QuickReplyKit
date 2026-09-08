using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;

namespace QuickReplyKit.Core.Text;

/// <summary>
/// Word-compatible converter for clipboard paste paths: parse stored HTML into a WPF
/// <see cref="FlowDocument"/> via the platform HTML importer and serialize to RTF.
/// Falls back to the project HTML parser if the platform import fails.
/// </summary>
public static class HtmlToRtf
{
    public static string Convert(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        try
        {
            var doc = LoadViaPlatform(html);
            using var ms = new MemoryStream();
            new TextRange(doc.ContentStart, doc.ContentEnd).Save(ms, DataFormats.Rtf);
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            return ConvertViaLegacy(html);
        }
    }

    private static string ConvertViaLegacy(string html)
    {
        try
        {
            var flow = HtmlToFlowDocument.Convert(html);
            using var ms = new MemoryStream();
            new TextRange(flow.ContentStart, flow.ContentEnd).Save(ms, DataFormats.Rtf);
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            return string.Empty;
        }
    }

    private static FlowDocument LoadViaPlatform(string html)
    {
        var doc = new FlowDocument
        {
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 13,
        };

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(WrapHtml(html)));
        var range = new TextRange(doc.ContentStart, doc.ContentEnd);
        range.Load(ms, DataFormats.Html);
        return doc;
    }

    private static string WrapHtml(string html)
    {
        if (html.Contains("<html", StringComparison.OrdinalIgnoreCase)) return html;
        return $"<html><head><meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\" /></head><body>{html}</body></html>";
    }
}
