using QuickReplyKit.Core.Text;
using System.Text;

namespace QuickReplyKit.Tests;

/// <summary>
/// Pure-string tests for <see cref="ClipboardHtml"/>: pulling the fragment out of Windows CF_HTML
/// and wrapping plain text into the subset. The subset-narrowing itself is covered by the
/// HtmlToFlowDocument round-trip tests, so here we only assert extraction and wrapping.
/// </summary>
public class ClipboardHtmlTests
{
    [Fact]
    public void ExtractFragment_PullsBetweenMarkers()
    {
        string cf =
            "Version:0.9\r\nStartHTML:00000097\r\nEndHTML:00000241\r\n" +
            "StartFragment:00000131\r\nEndFragment:00000209\r\n" +
            "<html><body><!--StartFragment--><p>hi <b>there</b></p><!--EndFragment--></body></html>";
        Assert.Equal("<p>hi <b>there</b></p>", ClipboardHtml.ExtractFragment(cf));
    }

    [Fact]
    public void ExtractFragment_NoMarkers_StripsVersionHeader()
    {
        string cf = "Version:0.9\r\nStartHTML:0\r\n<html><body><p>x</p></body></html>";
        Assert.Equal("<html><body><p>x</p></body></html>", ClipboardHtml.ExtractFragment(cf));
    }

    [Fact]
    public void ExtractFragment_UsesUtf8ByteOffsets_WhenMarkersAreMissing()
    {
        const string before = "<html><body>pré";
        const string fragment = "<p>selected ✓</p>";
        const string after = "after</body></html>";
        Assert.Equal(fragment, ClipboardHtml.ExtractFragment(WithOffsets(before, fragment, after)));
    }

    [Fact]
    public void ExtractFragment_ValidByteOffsetsOverrideUntrustedMarkerComments()
    {
        const string before = "<html><body><!--StartFragment--><p>wrong</p><!--EndFragment-->";
        const string fragment = "<p>selected</p>";
        Assert.Equal(fragment, ClipboardHtml.ExtractFragment(WithOffsets(before, fragment, "</body></html>")));
    }

    [Fact]
    public void ExtractFragment_PlainHtml_ReturnedAsIs()
    {
        Assert.Equal("<p>x</p>", ClipboardHtml.ExtractFragment("<p>x</p>"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ExtractFragment_EmptyIsEmpty(string? input) =>
        Assert.Equal("", ClipboardHtml.ExtractFragment(input));

    [Fact]
    public void FromPlainText_SingleLine_WrapsInParagraph() =>
        Assert.Equal("<p>hello</p>", ClipboardHtml.FromPlainText("hello"));

    [Fact]
    public void FromPlainText_BlankLineSplitsParagraphs() =>
        Assert.Equal("<p>a</p><p>b</p>", ClipboardHtml.FromPlainText("a\n\nb"));

    [Fact]
    public void FromPlainText_SingleNewlineBecomesBr() =>
        Assert.Equal("<p>a<br>b</p>", ClipboardHtml.FromPlainText("a\nb"));

    [Fact]
    public void FromPlainText_NormalizesCrLf() =>
        Assert.Equal("<p>a</p><p>b</p>", ClipboardHtml.FromPlainText("a\r\n\r\nb"));

    [Fact]
    public void FromPlainText_EscapesMarkup() =>
        Assert.Equal("<p>a &amp; &lt;b&gt;</p>", ClipboardHtml.FromPlainText("a & <b>"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FromPlainText_EmptyIsEmpty(string? input) =>
        Assert.Equal("", ClipboardHtml.FromPlainText(input));

    [Fact]
    public void Sanitize_RemovesActiveBlocksCommentsAndEventAttributes()
    {
        string html = "<!--secret--><p onclick=\"run()\">Hello<script>alert(1)</script> " +
            "<style>p{display:none}</style><b onmouseover=\"run()\">there</b></p>";

        Assert.Equal("<p>Hello <b>there</b></p>", ClipboardHtml.Sanitize(html));
    }

    [Theory]
    [InlineData("iframe")]
    [InlineData("object")]
    [InlineData("svg")]
    [InlineData("form")]
    [InlineData("template")]
    public void Sanitize_RemovesBlockedElementAndItsContent(string tag) =>
        Assert.Equal("<p>beforeafter</p>",
            ClipboardHtml.Sanitize($"<p>before<{tag}>hidden</{tag}>after</p>"));

    [Fact]
    public void Sanitize_DropsVoidActiveOrHiddenElements_WithoutDroppingFollowingText() =>
        Assert.Equal("<p>beforemiddleafter</p>",
            ClipboardHtml.Sanitize("<p>before<embed src='x'>middle<input hidden value='secret'>after</p>"));

    [Theory]
    [InlineData("hidden")]
    [InlineData("aria-hidden='true'")]
    [InlineData("style='display:none'")]
    [InlineData("style='visibility: hidden'")]
    [InlineData("style='opacity:0'")]
    public void Sanitize_RemovesHiddenContent(string attribute) =>
        Assert.Equal("<p>visible</p>",
            ClipboardHtml.Sanitize($"<p>visible<span {attribute}>secret</span></p>"));

    [Fact]
    public void Sanitize_RemovesCommonOfficeAndStylesheetHiddenContent()
    {
        string html = "<style>.css-hidden{display:/**/none}.collapsed{visibility:collapse}</style>" +
            "<p>visible<span style='mso-hide:all'>office</span>" +
            "<span class='css-hidden'>css</span><span class='collapsed'>collapsed</span></p>";

        Assert.Equal("<p>visible</p>", ClipboardHtml.Sanitize(html));
    }

    [Theory]
    [InlineData("display:none!important")]
    [InlineData("display:none ! important")]
    [InlineData("visibility:hidden !important")]
    [InlineData("visibility:hidden ! important")]
    [InlineData("mso-hide:all !IMPORTANT")]
    [InlineData("opacity:0%")]
    [InlineData(@"display:\6e one!\69 mportant")]
    public void Sanitize_RemovesImportantHiddenContent(string style) =>
        Assert.Equal("<p>visible</p>",
            ClipboardHtml.Sanitize($"<p>visible<span style='{style}'>secret</span></p>"));

    [Fact]
    public void Sanitize_DoesNotHidePartiallyTransparentContent() =>
        Assert.Equal("<p>visible</p>", ClipboardHtml.Sanitize("<p style='opacity:.5'>visible</p>"));

    [Fact]
    public void Sanitize_RemovesContentHiddenByStylesheetId() =>
        Assert.Equal("<p>visible</p>", ClipboardHtml.Sanitize(
            "<style>#secret{display:none}</style><p>visible<span id='secret'>secret</span></p>"));

    [Fact]
    public void Sanitize_NormalizesCssEscapesAndLeadingHyphenSelectors()
    {
        string html = @"<style>#\73 ecret{display:\6e one}.\68 ide{visibility:hidden}" +
            @".-hidden{opacity:0%}</style><p>visible<span id='secret'>id</span>" +
            "<span class='hide'>class</span><span class='-hidden'>hyphen</span></p>";

        Assert.Equal("<p>visible</p>", ClipboardHtml.Sanitize(html));
    }

    [Fact]
    public void Sanitize_TrimsAriaHiddenValue() =>
        Assert.Equal("<p>visible</p>",
            ClipboardHtml.Sanitize("<p>visible<span aria-hidden=' true '>secret</span></p>"));

    [Fact]
    public void Sanitize_RemovesSelfClosingSyntaxForBlockedContainer() =>
        Assert.Equal("<p>beforeafter</p>",
            ClipboardHtml.Sanitize("<p>before<script/>secret</script>after</p>"));

    [Fact]
    public void Sanitize_RemovesNestedHiddenElementsWithoutLeakingText() =>
        Assert.Equal("<p>visibleafter</p>",
            ClipboardHtml.Sanitize("<p>visible<span hidden>outer<span>inner</span>secret</span>after</p>"));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///c:/secret.txt")]
    [InlineData("data:text/html,boom")]
    public void Sanitize_StripsUnsafeLinkTarget_ButKeepsText(string uri) =>
        Assert.Equal("<p><a>open</a></p>", ClipboardHtml.Sanitize($"<p><a href=\"{uri}\">open</a></p>"));

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("mailto:support@example.com")]
    public void Sanitize_KeepsSafeLinks(string uri) =>
        Assert.Equal($"<p><a href=\"{uri}\">open</a></p>",
            ClipboardHtml.Sanitize($"<p><a href=\"{uri}\">open</a></p>"));

    [Fact]
    public void Sanitize_ParsesUnquotedAndEntityEncodedSafeLink() =>
        Assert.Equal("<p><a href=\"https://example.com/a?x=1&amp;y=2\">open</a></p>",
            ClipboardHtml.Sanitize("<p><a href=https://example.com/a?x=1&amp;y=2>open</a></p>"));

    [Fact]
    public void Sanitize_RejectsEntityEncodedUnsafeLink() =>
        Assert.Equal("<p><a>open</a></p>",
            ClipboardHtml.Sanitize("<p><a href=java&#x73;cript:alert(1)>open</a></p>"));

    [Fact]
    public void Sanitize_HandlesGreaterThanInsideQuotedAttribute() =>
        Assert.Equal("<p>visible</p>",
            ClipboardHtml.Sanitize("<p title='x>y'>visible</p>"));

    [Fact]
    public void Sanitize_DropsMalformedQuotedActiveBlock_WithoutLeakingItsContent() =>
        Assert.Equal("<p>vafter</p>",
            ClipboardHtml.Sanitize("<p>v<script data-x='>''>secret</script>after</p>"));

    [Fact]
    public void Sanitize_KeepsSupportedFormattingAndDropsUnsupportedStyles() =>
        Assert.Equal("<p style=\"text-align:center\"><span style=\"color:#FF0000\"><b>x</b></span></p>",
            ClipboardHtml.Sanitize("<p style='text-align:center;position:fixed'><span style='color:red;left:0'><b>x</b></span></p>"));

    [Fact]
    public void Sanitize_KeepsCodeAsEncodedVisibleText() =>
        Assert.Equal("<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>",
            ClipboardHtml.Sanitize("<pre>&lt;script&gt;alert(1)&lt;/script&gt;</pre>"));

    [Theory]
    [InlineData("<p>unterminated<script>bad")]
    [InlineData("<p title='>''>visible")]
    [InlineData("<!-- unterminated")]
    public void Sanitize_MalformedInput_NeverThrows(string html) =>
        Assert.Null(Record.Exception(() => ClipboardHtml.Sanitize(html)));

    [Fact]
    public void Sanitize_RejectsOversizedClipboardHtml() =>
        Assert.Equal("", ClipboardHtml.Sanitize(new string('x', ClipboardHtml.MaxInputChars + 1)));

    [Fact]
    public void Sanitize_RejectsExcessiveNestingBeforeWpfConversion()
    {
        string html = string.Concat(Enumerable.Repeat("<b>", ClipboardHtml.MaxDepth + 1)) + "x";
        Assert.Equal("", ClipboardHtml.Sanitize(html));
    }

    [Fact]
    public void Sanitize_RejectsExcessiveTagCountBeforeWpfConversion()
    {
        string html = "<p>" + string.Concat(Enumerable.Repeat("<b>x</b>", ClipboardHtml.MaxTags / 2)) + "</p>";
        Assert.Equal("", ClipboardHtml.Sanitize(html));
    }

    [Fact]
    public void FromPlainText_RejectsOversizedClipboardText() =>
        Assert.Equal("", ClipboardHtml.FromPlainText(new string('x', ClipboardHtml.MaxInputChars + 1)));

    private static string WithOffsets(string before, string fragment, string after)
    {
        string header = "Version:0.9\r\nStartHTML:AAAAAAAAAA\r\nEndHTML:BBBBBBBBBB\r\n" +
            "StartFragment:CCCCCCCCCC\r\nEndFragment:DDDDDDDDDD\r\n";
        int startHtml = Encoding.UTF8.GetByteCount(header);
        int startFragment = startHtml + Encoding.UTF8.GetByteCount(before);
        int endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        int endHtml = endFragment + Encoding.UTF8.GetByteCount(after);
        return header
            .Replace("AAAAAAAAAA", startHtml.ToString("D10"))
            .Replace("BBBBBBBBBB", endHtml.ToString("D10"))
            .Replace("CCCCCCCCCC", startFragment.ToString("D10"))
            .Replace("DDDDDDDDDD", endFragment.ToString("D10")) + before + fragment + after;
    }
}
