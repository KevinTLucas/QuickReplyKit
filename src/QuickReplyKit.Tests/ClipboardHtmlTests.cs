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
}
