using QuickReplyKit.Core.Text;

namespace QuickReplyKit.Tests;

public class HtmlTextTests
{
    [Fact]
    public void Paragraphs_BecomeNewlines()
    {
        Assert.Equal("Hello\nWorld", HtmlText.ToPlainText("<p>Hello</p><p>World</p>"));
    }

    [Fact]
    public void LineBreaks_BecomeNewlines()
    {
        Assert.Equal("Line1\nLine2", HtmlText.ToPlainText("<p>Line1<br>Line2</p>"));
    }

    [Fact]
    public void UnorderedList_UsesBullets()
    {
        Assert.Equal("• a\n• b", HtmlText.ToPlainText("<ul><li>a</li><li>b</li></ul>"));
    }

    [Fact]
    public void OrderedList_UsesNumbers()
    {
        Assert.Equal("1. a\n2. b\n3. c", HtmlText.ToPlainText("<ol><li>a</li><li>b</li><li>c</li></ol>"));
    }

    [Fact]
    public void Entities_AreDecoded()
    {
        Assert.Equal("A & B <tag> \"q\" 'x'",
            HtmlText.ToPlainText("<p>A &amp; B &lt;tag&gt; &quot;q&quot; &#39;x&#39;</p>"));
    }

    [Fact]
    public void FormattingTags_AreStripped_TextKept()
    {
        Assert.Equal("Bold and both",
            HtmlText.ToPlainText("<p><b>Bold</b> and <i><u>both</u></i></p>"));
    }

    [Fact]
    public void Hyperlink_KeepsInnerText()
    {
        Assert.Equal("See link",
            HtmlText.ToPlainText("<p>See <a href=\"https://example.com/\">link</a></p>"));
    }

    [Fact]
    public void Mixed_ParagraphsAndList()
    {
        var html = "<p>Steps:</p><ol><li>First</li><li>Second</li></ol><p>Done</p>";
        Assert.Equal("Steps:\n1. First\n2. Second\nDone", HtmlText.ToPlainText(html));
    }

    [Fact]
    public void NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal("", HtmlText.ToPlainText(null));
        Assert.Equal("", HtmlText.ToPlainText(""));
    }
}
