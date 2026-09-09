using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;

namespace QuickReplyKit.Core.Text;

/// <summary>
/// Word-compatible converter for clipboard paste paths: parse stored HTML into a WPF
/// <see cref="FlowDocument"/> with the same parser used by the editor and serialize it to RTF.
/// </summary>
public static class HtmlToRtf
{
    public static string Convert(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        try
        {
            var doc = HtmlToFlowDocument.Convert(html);
            using var ms = new MemoryStream();
            new TextRange(doc.ContentStart, doc.ContentEnd).Save(ms, DataFormats.Rtf);
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            return string.Empty;
        }
    }
}
