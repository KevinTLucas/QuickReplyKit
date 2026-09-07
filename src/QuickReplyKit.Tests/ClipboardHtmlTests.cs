using QuickReplyKit.Core.Text;

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
}
