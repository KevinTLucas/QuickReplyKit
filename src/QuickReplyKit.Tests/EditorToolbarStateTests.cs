using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using QuickReplyKit.Views;

namespace QuickReplyKit.Tests;

public class EditorToolbarStateTests
{
    [Theory]
    [InlineData(TextMarkerStyle.Disc)]
    [InlineData(TextMarkerStyle.Decimal)]
    public void ListState_MixedInteriorParagraph_IsIndeterminate(TextMarkerStyle marker) => Sta(() =>
    {
        var first = Paragraph("first");
        var middle = Paragraph("plain");
        var last = Paragraph("last");
        var document = new FlowDocument();
        document.Blocks.Add(List(marker, first));
        document.Blocks.Add(middle);
        document.Blocks.Add(List(marker, last));

        var state = EditorWindow.ListState(new TextRange(first.ContentStart, last.ContentEnd));

        Assert.Null(state.Bullet);
        Assert.Null(state.Number);
    });

    [Theory]
    [InlineData(TextMarkerStyle.Disc, true, false)]
    [InlineData(TextMarkerStyle.Decimal, false, true)]
    public void ListState_UniformMultiItemList_IsChecked(
        TextMarkerStyle marker, bool bullet, bool number) => Sta(() =>
    {
        var first = Paragraph("first");
        var second = Paragraph("second");
        var document = new FlowDocument(List(marker, first, second));

        var state = EditorWindow.ListState(new TextRange(first.ContentStart, second.ContentEnd));

        Assert.Equal(bullet, state.Bullet);
        Assert.Equal(number, state.Number);
    });

    [Theory]
    [InlineData(TextMarkerStyle.Disc, true, false)]
    [InlineData(TextMarkerStyle.Decimal, false, true)]
    public void ListState_CollapsedCaret_PreservesCurrentList(
        TextMarkerStyle marker, bool bullet, bool number) => Sta(() =>
    {
        var paragraph = Paragraph("item");
        var document = new FlowDocument(List(marker, paragraph));

        var state = EditorWindow.ListState(new TextRange(paragraph.ContentStart, paragraph.ContentStart));

        Assert.Equal(bullet, state.Bullet);
        Assert.Equal(number, state.Number);
    });

    [Fact]
    public void ListState_CollapsedCaretInPlainParagraph_IsOff() => Sta(() =>
    {
        var paragraph = Paragraph("plain");
        var document = new FlowDocument(paragraph);

        var state = EditorWindow.ListState(new TextRange(paragraph.ContentStart, paragraph.ContentStart));

        Assert.False(state.Bullet);
        Assert.False(state.Number);
    });

    private static Paragraph Paragraph(string text) => new(new Run(text));

    private static List List(TextMarkerStyle marker, params Paragraph[] paragraphs)
    {
        var list = new List { MarkerStyle = marker };
        foreach (var paragraph in paragraphs) list.ListItems.Add(new ListItem(paragraph));
        return list;
    }

    private static void Sta(Action test)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
