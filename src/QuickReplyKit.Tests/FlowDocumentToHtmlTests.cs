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
    // Widened subset (Phase 10a): strike, colour, highlight, font-size, alignment.
    [InlineData("<p>a <s>struck</s> b</p>")]
    [InlineData("<p><span style=\"color:#E11D48\">red</span></p>")]
    [InlineData("<p><span style=\"background-color:#FEF08A\">hi</span></p>")]
    [InlineData("<p><span style=\"font-size:11px\">small</span></p>")]
    [InlineData("<p><span style=\"font-size:18px\">large</span></p>")]
    [InlineData("<p><span style=\"font-size:24px\">heading</span></p>")]
    [InlineData("<p><span style=\"color:#2563EB; background-color:#FEF08A; font-size:18px\">all</span></p>")]
    [InlineData("<p style=\"text-align:center\">centred</p>")]
    [InlineData("<p style=\"text-align:right\">right</p>")]
    [InlineData("<p style=\"text-align:justify\">just</p>")]
    [InlineData("<p>a <s><span style=\"color:#16A34A\">green struck</span></s> b</p>")]
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
    public void Convert_DropsFontFamilyAndRunWeight_KeepsColorAndSize()
    {
        Sta(() =>
        {
            var run = new Run("hi")
            {
                Foreground = Brushes.Red,        // kept (Phase 10a): -> color:#FF0000
                FontSize = 40,                   // kept (Phase 10a): -> font-size:40px
                FontFamily = new FontFamily("Comic Sans MS"),  // outside the subset: dropped
                FontWeight = FontWeights.Bold,   // a Run property, not a <Bold> element: dropped
            };
            var doc = new FlowDocument(new Paragraph(run));
            Assert.Equal("<p><span style=\"color:#FF0000; font-size:40px\">hi</span></p>",
                FlowDocumentToHtml.Convert(doc));
        });
    }

    [Fact]
    public void Convert_Null_ReturnsEmpty()
    {
        Assert.Equal("", FlowDocumentToHtml.Convert(null));
    }

    // --- Widened subset: emit new formatting set locally on the FlowDocument (as the editor does) ---

    [Fact]
    public void Convert_EmitsColorHighlightAndSize_FromLocalRunProperties()
    {
        Sta(() =>
        {
            var run = new Run("x") { Foreground = Brushes.Red, Background = Brushes.Yellow, FontSize = 18 };
            var doc = new FlowDocument(new Paragraph(run));
            Assert.Equal("<p><span style=\"color:#FF0000; background-color:#FFFF00; font-size:18px\">x</span></p>",
                FlowDocumentToHtml.Convert(doc));
        });
    }

    // "Automatic (theme default)" in the editor clears the explicit foreground via UnsetValue so the
    // text inherits the theme colour (readable in dark). Verify the clear reaches the serialized HTML.
    [Fact]
    public void ApplyUnsetForeground_ClearsColor_ForAutomaticThemeDefault()
    {
        Sta(() =>
        {
            var rtb = new System.Windows.Controls.RichTextBox
            {
                Document = new FlowDocument(new Paragraph(new Run("hi"))),
            };
            rtb.SelectAll();
            rtb.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.Red);
            Assert.Contains("color:#FF0000", FlowDocumentToHtml.Convert(rtb.Document));

            // Same technique EditorWindow.ClearForeground uses (UnsetValue throws): split then clear.
            rtb.SelectAll();
            for (var p = rtb.Selection.Start; p != null && p.CompareTo(rtb.Selection.End) < 0;
                 p = p.GetNextContextPosition(LogicalDirection.Forward))
                (p.Parent as Run)?.ClearValue(TextElement.ForegroundProperty);
            Assert.DoesNotContain("color:", FlowDocumentToHtml.Convert(rtb.Document));
        });
    }

    [Fact]
    public void Convert_NormalSize_EmitsNoFontSpan()
    {
        Sta(() =>
        {
            var run = new Run("x") { FontSize = FlowDocumentToHtml.NormalFontSize };
            var doc = new FlowDocument(new Paragraph(run));
            Assert.Equal("<p>x</p>", FlowDocumentToHtml.Convert(doc));
        });
    }

    [Fact]
    public void Convert_TransparentHighlight_IsDropped()
    {
        Sta(() =>
        {
            var run = new Run("x") { Background = Brushes.Transparent };
            var doc = new FlowDocument(new Paragraph(run));
            Assert.Equal("<p>x</p>", FlowDocumentToHtml.Convert(doc));
        });
    }

    [Fact]
    public void Convert_EmitsStrikethrough()
    {
        Sta(() =>
        {
            var span = new Span(new Run("gone")) { TextDecorations = TextDecorations.Strikethrough };
            var doc = new FlowDocument(new Paragraph(span));
            Assert.Equal("<p><s>gone</s></p>", FlowDocumentToHtml.Convert(doc));
        });
    }

    [Fact]
    public void Convert_EmitsAlignment()
    {
        Sta(() =>
        {
            var doc = new FlowDocument(new Paragraph(new Run("mid")) { TextAlignment = TextAlignment.Center });
            Assert.Equal("<p style=\"text-align:center\">mid</p>", FlowDocumentToHtml.Convert(doc));
        });
    }

    // Back-compat: an old-subset-only response (only the original tags) round-trips byte-for-byte.
    [Theory]
    [InlineData("<p>Hi <b>there</b>, <i>welcome</i> to <u>IXL</u>.</p>")]
    [InlineData("<p>Steps:</p><ol><li>First</li><li>Second</li></ol><p>See <a href=\"https://ixl.com/\">IXL</a></p>")]
    [InlineData("<ul><li>outer<ul><li>inner</li></ul></li></ul>")]
    public void OldSubsetOnly_RoundTripsUnchanged(string html)
    {
        Sta(() => Assert.Equal(html, FlowDocumentToHtml.Convert(HtmlToFlowDocument.Convert(html))));
    }
}
