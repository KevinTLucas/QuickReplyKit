using System.Threading;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
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

    [Fact]
    public void NestedLists_ProduceAListWithinAListItem()
    {
        Sta(() =>
        {
            var doc = HtmlToFlowDocument.Convert("<ul><li>outer<ul><li>inner</li></ul></li></ul>");
            var outer = doc.Blocks.OfType<List>().Single();
            var outerItem = outer.ListItems.Single();
            var inner = outerItem.Blocks.OfType<List>().Single();
            Assert.Single(inner.ListItems);
            Assert.Contains("outer", Text(doc));
            Assert.Contains("inner", Text(doc));
        });
    }

    [Fact]
    public void Link_WithoutHref_RendersTextWithNoNavigateUri()
    {
        Sta(() =>
        {
            var doc = HtmlToFlowDocument.Convert("<p>see <a>plain</a> here</p>");
            var link = doc.Blocks.OfType<Paragraph>().Single().Inlines.OfType<Hyperlink>().Single();
            Assert.Null(link.NavigateUri);                     // not clickable, but text survives
            Assert.Equal("plain", new TextRange(link.ContentStart, link.ContentEnd).Text);
        });
    }

    [Fact]
    public void UnknownTags_AreIgnored_ButInnerTextIsKept()
    {
        Sta(() =>
        {
            var doc = HtmlToFlowDocument.Convert("<div><span>hello</span> <mark>world</mark></div>");
            Assert.Contains("hello world", Text(doc));
        });
    }

    // --- Widened subset (Phase 10a) ---

    [Fact]
    public void Strikethrough_ProducesStruckSpan()
    {
        Sta(() =>
        {
            var doc = HtmlToFlowDocument.Convert("<p><s>gone</s></p>");
            var span = doc.Blocks.OfType<Paragraph>().Single().Inlines.OfType<Span>().Single();
            Assert.Contains(span.TextDecorations, d => d.Location == TextDecorationLocation.Strikethrough);
        });
    }

    [Fact]
    public void SpanStyle_AppliesColorHighlightAndSize()
    {
        Sta(() =>
        {
            var doc = HtmlToFlowDocument.Convert(
                "<p><span style=\"color:#2563EB; background-color:#FEF08A; font-size:18px\">x</span></p>");
            var span = doc.Blocks.OfType<Paragraph>().Single().Inlines.OfType<Span>().Single();
            Assert.Equal(Color.FromRgb(0x25, 0x63, 0xEB), ((SolidColorBrush)span.Foreground).Color);
            Assert.Equal(Color.FromRgb(0xFE, 0xF0, 0x8A), ((SolidColorBrush)span.Background).Color);
            Assert.Equal(18, span.FontSize);
        });
    }

    [Fact]
    public void ParagraphAlignment_IsApplied()
    {
        Sta(() =>
        {
            var doc = HtmlToFlowDocument.Convert("<p style=\"text-align:center\">mid</p><p style=\"text-align:right\">r</p>");
            var paras = doc.Blocks.OfType<Paragraph>().ToList();
            Assert.Equal(TextAlignment.Center, paras[0].TextAlignment);
            Assert.Equal(TextAlignment.Right, paras[1].TextAlignment);
        });
    }

    [Theory]
    [InlineData("<p><span style=\"font-size:oops\">x</span></p>")]
    [InlineData("<p><span style=\"color:notacolor\">x</span></p>")]
    [InlineData("<p><span style=\"garbage\">x</span></p>")]
    [InlineData("<p style=\"text-align:sideways\">x</p>")]
    public void MalformedNewStyles_DoNotThrow_KeepText(string html)
    {
        Sta(() =>
        {
            var doc = HtmlToFlowDocument.Convert(html);
            Assert.Contains("x", Text(doc));
        });
    }
}
