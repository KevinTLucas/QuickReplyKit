using System.Threading;
using System.Windows;
using System.Windows.Documents;
using QuickReplyKit.Core.Text;

namespace QuickReplyKit.Tests;

/// <summary>
/// WPF <see cref="FlowDocument"/> objects are DispatcherObjects and must be built on an STA
/// thread, so every assertion runs inside <see cref="Sta"/>.
/// </summary>
public class HtmlToFlowDocumentTests
{
    private static void Sta(Action test)
    {
        Exception? captured = null;
        var t = new Thread(() =>
        {
            try { test(); }
            catch (Exception e) { captured = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        t.Join();
        if (captured != null) throw new Exception("STA test body threw", captured);
    }

    private static string Text(FlowDocument doc) =>
        new TextRange(doc.ContentStart, doc.ContentEnd).Text;

    [Fact]
    public void Paragraphs_BecomeSeparateBlocks()
    {
        Sta(() =>
        {
            var doc = HtmlToFlowDocument.Convert("<p>Hello</p><p>World</p>");
            var paras = doc.Blocks.OfType<Paragraph>().ToList();
            Assert.Equal(2, paras.Count);
            Assert.Equal("Hello", new TextRange(paras[0].ContentStart, paras[0].ContentEnd).Text);
            Assert.Equal("World", new TextRange(paras[1].ContentStart, paras[1].ContentEnd).Text);
        });
    }

    [Fact]
    public void Bold_ProducesBoldInline()
    {
        Sta(() =>
        {
            var doc = HtmlToFlowDocument.Convert("<p>a <b>bold</b> c</p>");
            var para = doc.Blocks.OfType<Paragraph>().Single();
            Assert.Contains(para.Inlines, x => x is Bold b &&
                new TextRange(b.ContentStart, b.ContentEnd).Text == "bold");
        });
    }

    [Fact]
    public void ItalicAndUnderline_ProduceTheirInlines()
    {
        Sta(() =>
        {
            var doc = HtmlToFlowDocument.Convert("<p><i>it</i><em>em</em><u>un</u></p>");
            var para = doc.Blocks.OfType<Paragraph>().Single();
            Assert.Equal(2, para.Inlines.OfType<Italic>().Count());  // <i> and <em>
            Assert.Single(para.Inlines.OfType<Underline>());
        });
    }

    [Fact]
    public void LineBreak_BecomesLineBreakInline()
    {
        Sta(() =>
        {
            var doc = HtmlToFlowDocument.Convert("<p>a<br>b</p>");
            var para = doc.Blocks.OfType<Paragraph>().Single();
            Assert.Single(para.Inlines.OfType<LineBreak>());
        });
    }

    [Fact]
    public void UnorderedList_BecomesDiscList()
    {
        Sta(() =>
        {
            var doc = HtmlToFlowDocument.Convert("<ul><li>one</li><li>two</li></ul>");
            var list = doc.Blocks.OfType<List>().Single();
            Assert.Equal(TextMarkerStyle.Disc, list.MarkerStyle);
            Assert.Equal(2, list.ListItems.Count);
        });
    }

    [Fact]
    public void OrderedList_BecomesDecimalList()
    {
        Sta(() =>
        {
            var doc = HtmlToFlowDocument.Convert("<ol><li>one</li><li>two</li></ol>");
            var list = doc.Blocks.OfType<List>().Single();
            Assert.Equal(TextMarkerStyle.Decimal, list.MarkerStyle);
            Assert.Equal(2, list.ListItems.Count);
        });
    }

    [Fact]
    public void Hyperlink_KeepsUriAndText()
    {
        Sta(() =>
        {
            var doc = HtmlToFlowDocument.Convert("<p>see <a href=\"https://ixl.com/\">IXL</a></p>");
            var para = doc.Blocks.OfType<Paragraph>().Single();
            var link = para.Inlines.OfType<Hyperlink>().Single();
            Assert.Equal("https://ixl.com/", link.NavigateUri?.AbsoluteUri);
            Assert.Equal("IXL", new TextRange(link.ContentStart, link.ContentEnd).Text);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("<p><b>unclosed")]
    [InlineData("plain text, no tags")]
    [InlineData("<<>><ul><li>x</ol></p>")]
    [InlineData("<a href=broken>oops</a>")]
    [InlineData("<p>tom & jerry &amp; co</p>")]
    public void MalformedOrOddInput_DoesNotThrow(string html)
    {
        Sta(() =>
        {
            var doc = HtmlToFlowDocument.Convert(html);
            Assert.NotNull(doc);
            _ = Text(doc);  // reading the content must not throw either
        });
    }

    [Fact]
    public void PlainText_AroundTags_IsPreserved()
    {
        Sta(() =>
        {
            var doc = HtmlToFlowDocument.Convert("<p>Hi <b>there</b>, welcome</p>");
            Assert.Contains("Hi there, welcome", Text(doc));
        });
    }
}
