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
public class FlowDocumentToHtmlTests
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

    // Canonical subset HTML must survive HtmlToFlowDocument -> FlowDocumentToHtml unchanged.
    [Theory]
    [InlineData("<p>Hello</p><p>World</p>")]
    [InlineData("<p>a <b>bold</b> c</p>")]
    [InlineData("<p><i>it</i></p>")]
    [InlineData("<p><u>un</u></p>")]
    [InlineData("<p>a <b>bold <i>italic</i></b> <u>under</u></p>")]
    [InlineData("<p>a<br>b</p>")]
    [InlineData("<ul><li>one</li><li>two</li></ul>")]
    [InlineData("<ol><li>one</li><li>two</li></ol>")]
    [InlineData("<ul><li>outer<ul><li>inner</li></ul></li></ul>")]
    [InlineData("<p>see <a href=\"https://ixl.com/\">IXL</a></p>")]
    [InlineData("<p>see <a>plain</a> here</p>")]
    public void RoundTrip_Subset_IsStable(string html)
    {
        Sta(() =>
        {
            string once = FlowDocumentToHtml.Convert(HtmlToFlowDocument.Convert(html));
            Assert.Equal(html, once);
            // Idempotent: a second pass changes nothing.
            string twice = FlowDocumentToHtml.Convert(HtmlToFlowDocument.Convert(once));
            Assert.Equal(once, twice);
        });
    }

    [Fact]
    public void Convert_EscapesTextSpecialChars()
    {
        Sta(() =>
        {
            var doc = new FlowDocument(new Paragraph(new Run("a < b & c > d")));
            Assert.Equal("<p>a &lt; b &amp; c &gt; d</p>", FlowDocumentToHtml.Convert(doc));
        });
    }

    [Fact]
    public void Convert_DropsColorsFontsAndSizes()
    {
        Sta(() =>
        {
            var run = new Run("hi")
            {
                Foreground = Brushes.Red,
                FontSize = 40,
                FontFamily = new FontFamily("Comic Sans MS"),
                FontWeight = FontWeights.Bold,   // a Run property, not a <Bold> element — dropped
            };
            var doc = new FlowDocument(new Paragraph(run));
            Assert.Equal("<p>hi</p>", FlowDocumentToHtml.Convert(doc));
        });
    }

    [Fact]
    public void Convert_Null_ReturnsEmpty()
    {
        Assert.Equal("", FlowDocumentToHtml.Convert(null));
    }
}
