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
