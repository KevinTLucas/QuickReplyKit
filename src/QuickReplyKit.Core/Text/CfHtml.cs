using System.Text;

namespace QuickReplyKit.Core.Text;

/// <summary>
/// Builds a CF_HTML clipboard payload. The header carries byte offsets (UTF-8) into the
/// payload; we zero-pad each to a fixed 10 digits so the header length is constant and the
/// offsets can be computed in one pass. Consumed by the Phase 3 paste engine — must be exact.
/// </summary>
public static class CfHtml
{
    private const string HeaderFormat =
        "Version:0.9\r\n" +
        "StartHTML:{0:D10}\r\n" +
        "EndHTML:{1:D10}\r\n" +
        "StartFragment:{2:D10}\r\n" +
        "EndFragment:{3:D10}\r\n";

    private const string Prefix = "<html><body><!--StartFragment-->";
    private const string Suffix = "<!--EndFragment--></body></html>";

    public static string Build(string fragmentHtml)
    {
        fragmentHtml ??= "";

        // Header length is constant (fixed 10-digit fields), so measuring it with zeros
        // gives the true offset of the first real byte after the header.
        int headerLen = Utf8Len(string.Format(HeaderFormat, 0, 0, 0, 0));
        int startHtml = headerLen;
        int startFragment = startHtml + Utf8Len(Prefix);
        int endFragment = startFragment + Utf8Len(fragmentHtml);
        int endHtml = endFragment + Utf8Len(Suffix);

        return string.Format(HeaderFormat, startHtml, endHtml, startFragment, endFragment)
             + Prefix + fragmentHtml + Suffix;
    }

    private static int Utf8Len(string s) => Encoding.UTF8.GetByteCount(s);
}
