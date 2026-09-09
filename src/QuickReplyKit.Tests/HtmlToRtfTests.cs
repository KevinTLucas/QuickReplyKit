using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using QuickReplyKit.Core.Text;

namespace QuickReplyKit.Tests;

public class HtmlToRtfTests
{
    private static void Sta(Action test)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { captured = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        if (captured != null) throw new Exception("STA test body threw", captured);
    }

    [Fact]
    public void Convert_ReturnsParseableRtf() => Sta(() =>
    {
        const string html = "<p><b>Bold</b> <i>Italic</i> <span style=\"font-size:24px\">Big</span> <span style=\"color:#cc0000\">Red</span></p>";
        var rtf = HtmlToRtf.Convert(html);

        Assert.False(string.IsNullOrWhiteSpace(rtf));
        Assert.Contains(@"{\rtf1", rtf);

        var doc = new FlowDocument();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rtf));
        new TextRange(doc.ContentStart, doc.ContentEnd).Load(stream, DataFormats.Rtf);

        var boldRun = Runs(doc).FirstOrDefault(r => r.Text.Contains("Bold"));
        Assert.NotNull(boldRun);
        Assert.Equal(FontWeights.Bold, boldRun!.FontWeight);

        var italicRun = Runs(doc).FirstOrDefault(r => r.Text.Contains("Italic"));
        Assert.NotNull(italicRun);
        Assert.Equal(FontStyles.Italic, italicRun!.FontStyle);

        var bigRun = Runs(doc).FirstOrDefault(r => r.Text.Contains("Big"));
        Assert.NotNull(bigRun);
        Assert.True(bigRun!.FontSize >= 18);

        var redRun = Runs(doc).FirstOrDefault(r => r.Text.Contains("Red"));
        Assert.NotNull(redRun);
        Assert.NotNull(redRun!.Foreground);
        var redBrush = redRun.Foreground as SolidColorBrush;
        Assert.NotNull(redBrush);
        Assert.NotEqual(Colors.Black, redBrush!.Color);
    });

    [Fact]
    public void Convert_GracefullyReturnsEmptyForBlankInput() => Sta(() =>
    {
        Assert.Empty(HtmlToRtf.Convert("  "));
    });

    [Fact]
    public void Convert_PreservesEveryInlineEditorFormat() => Sta(() =>
    {
        const string html = "<p><b>Bold</b> <i>Italic</i> <u>Underline</u> <s>Strike</s> " +
            "<b><i><u><s>Combined</s></u></i></b> " +
            "<span style=\"font-size:11px\">Small</span> <span style=\"font-size:18px\">Large</span> " +
            "<span style=\"font-size:24px\">Heading</span></p>";
        var doc = RoundTrip(html);

        Assert.Equal(FontWeights.Bold, FindRun(doc, "Bold").FontWeight);
        Assert.Equal(FontStyles.Italic, FindRun(doc, "Italic").FontStyle);
        string normalized = FlowDocumentToHtml.Convert(doc);
        Assert.Contains("<u>Underline</u>", normalized);
        Assert.Contains("<s>Strike</s>", normalized);
        Assert.Equal(FontWeights.Bold, FindRun(doc, "Combined").FontWeight);
        Assert.Equal(FontStyles.Italic, FindRun(doc, "Combined").FontStyle);
        Assert.Contains("<u><s>Combined</s></u>", normalized);
        // RTF stores half-points; 11 WPF DIPs (8.25 pt) rounds to the nearest representable size.
        Assert.InRange(FindRun(doc, "Small").FontSize, 10.66, 11.34);
        Assert.Equal(18, FindRun(doc, "Large").FontSize, precision: 3);
        Assert.Equal(24, FindRun(doc, "Heading").FontSize, precision: 3);
    });

    [Fact]
    public void Convert_PreservesEveryEditorPaletteValue() => Sta(() =>
    {
        string[] textColors = ["#111111", "#6B7280", "#DC2626", "#EA580C", "#16A34A", "#0891B2", "#2563EB", "#7C3AED"];
        string[] highlights = ["#FEF08A", "#BBF7D0", "#BFDBFE", "#FBCFE8", "#FED7AA", "#E9D5FF"];
        string html = "<p>" + string.Concat(textColors.Select((color, i) =>
                $"<span style=\"color:{color}\">Text{i}</span> ")) +
            string.Concat(highlights.Select((color, i) =>
                $"<span style=\"background-color:{color}\">Mark{i}</span> ")) + "</p>";
        var doc = RoundTrip(html);
        string normalized = FlowDocumentToHtml.Convert(doc);

        for (int i = 0; i < textColors.Length; i++)
            Assert.Contains($"color:{textColors[i]}", normalized, StringComparison.OrdinalIgnoreCase);
        for (int i = 0; i < highlights.Length; i++)
            Assert.Contains($"background-color:{highlights[i]}", normalized, StringComparison.OrdinalIgnoreCase);
    });

    [Fact]
    public void Convert_PreservesBlocksLinksAndUnicode() => Sta(() =>
    {
        const string html = "<p style=\"text-align:center\">Centered<br>café 日本語 😀</p>" +
            "<p style=\"text-align:right\"><a href=\"https://example.com/help\">Help link</a></p>" +
            "<ul><li>Bullet one</li></ul><ol><li>Number one</li></ol>";
        var doc = RoundTrip(html);
        var paragraphs = Paragraphs(doc).ToList();

        Assert.Equal(TextAlignment.Center, paragraphs.Single(p => Text(p).Contains("Centered")).TextAlignment);
        Assert.Equal(TextAlignment.Right, paragraphs.Single(p => Text(p).Contains("Help link")).TextAlignment);
        Assert.Contains("café 日本語 😀", Text(paragraphs.Single(p => Text(p).Contains("Centered"))));
        Assert.Contains(paragraphs.SelectMany(p => p.Inlines).OfType<Hyperlink>(),
            link => link.NavigateUri?.AbsoluteUri == "https://example.com/help");
        var lists = doc.Blocks.OfType<System.Windows.Documents.List>().ToList();
        Assert.Contains(lists, list => list.MarkerStyle == TextMarkerStyle.Disc && Text(list).Contains("Bullet one"));
        Assert.Contains(lists, list => list.MarkerStyle == TextMarkerStyle.Decimal && Text(list).Contains("Number one"));
    });

    [Fact]
    public void Convert_UsesEditorNormalFontSize() => Sta(() =>
    {
        var doc = RoundTrip("<p>Normal</p>");
        Assert.Equal(FlowDocumentToHtml.NormalFontSize, FindRun(doc, "Normal").FontSize, precision: 3);
    });

    private static FlowDocument RoundTrip(string html)
    {
        var doc = new FlowDocument();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(HtmlToRtf.Convert(html)));
        new TextRange(doc.ContentStart, doc.ContentEnd).Load(stream, DataFormats.Rtf);
        return doc;
    }

    private static Run FindRun(FlowDocument doc, string text) => Runs(doc).Single(r => r.Text.Contains(text));

    private static IEnumerable<Paragraph> Paragraphs(FlowDocument doc) =>
        doc.Blocks.SelectMany(Paragraphs);

    private static IEnumerable<Paragraph> Paragraphs(Block block) => block switch
    {
        Paragraph paragraph => new[] { paragraph },
        System.Windows.Documents.List list => list.ListItems.SelectMany(item => item.Blocks.SelectMany(Paragraphs)),
        _ => Enumerable.Empty<Paragraph>(),
    };

    private static string Text(TextElement element) => new TextRange(element.ContentStart, element.ContentEnd).Text;

    private static System.Collections.Generic.IEnumerable<Run> Runs(FlowDocument doc)
    {
        foreach (var block in doc.Blocks)
        {
            if (block is Paragraph paragraph)
            {
                foreach (var run in RunsFromInlines(paragraph.Inlines))
                {
                    yield return run;
                }
            }
            else if (block is System.Windows.Documents.List list)
            {
                foreach (var listItem in list.ListItems.Cast<ListItem>())
                {
                    foreach (var listBlock in listItem.Blocks)
                    {
                        foreach (var run in listBlock switch
                        {
                            Paragraph p => RunsFromInlines(p.Inlines),
                            System.Windows.Documents.List l => l.ListItems.Cast<ListItem>().SelectMany(i => i.Blocks.SelectMany(x =>
                                x is Paragraph lp ? RunsFromInlines(lp.Inlines) :
                                System.Linq.Enumerable.Empty<Run>())),
                            _ => System.Linq.Enumerable.Empty<Run>(),
                        })
                        {
                            yield return run;
                        }
                    }
                }
            }
        }
    }

    private static System.Collections.Generic.IEnumerable<Run> RunsFromInlines(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run) yield return run;
            else if (inline is Span span)
            {
                foreach (var child in RunsFromInlines(span.Inlines))
                {
                    yield return child;
                }
            }
        }
    }
}
