using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.Core.Text;
using QuickReplyKit.Views;

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
    [InlineData("<p><u><s>underlined strike</s></u></p>")]
    public void RoundTrip_Subset_IsStable(string html)
    {
        Sta(() =>
        {
            string once = FlowDocumentToHtml.Convert(HtmlToFlowDocument.Convert(html));
            string expected = html == "<p>a <b>bold <i>italic</i></b> <u>under</u></p>"
                ? "<p>a <b>bold </b><b><i>italic</i></b> <u>under</u></p>"
                : html;
            Assert.Equal(expected, once);
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
    public void Convert_DropsFontFamily_KeepsRunWeightColorAndSize()
    {
        Sta(() =>
        {
            var run = new Run("hi")
            {
                Foreground = Brushes.Red,        // kept (Phase 10a): -> color:#FF0000
                FontSize = 40,                   // kept (Phase 10a): -> font-size:40px
                FontFamily = new FontFamily("Comic Sans MS"),  // outside the subset: dropped
                FontWeight = FontWeights.Bold,
            };
            var doc = new FlowDocument(new Paragraph(run));
            Assert.Equal("<p><b><span style=\"color:#FF0000; font-size:40px\">hi</span></b></p>",
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

    [Fact]
    public void EditorCommands_BoldItalicUnderline_SaveAndReload()
    {
        Sta(() =>
        {
            var editor = new RichTextBox { Document = new FlowDocument(new Paragraph(new Run("formatted"))) };
            editor.SelectAll();
            EditingCommands.ToggleBold.Execute(null, editor);
            EditingCommands.ToggleItalic.Execute(null, editor);
            EditingCommands.ToggleUnderline.Execute(null, editor);

            string html = FlowDocumentToHtml.Convert(editor.Document);
            Assert.Equal("<p><b><i><u>formatted</u></i></b></p>", html);

            using var temp = new TempDir();
            ResponseFile.Save(new ResponseItem { Name = "Formatted", Html = html }, temp.Path);
            string loadedHtml = ResponseFile.Load(temp.Of("Formatted.json"), temp.Path).Html;
            Assert.Equal(html, loadedHtml);
            var loaded = HtmlToFlowDocument.Convert(loadedHtml);
            var loadedRun = Runs(loaded).Single(r => r.Text == "formatted");
            Assert.Equal(FontWeights.Bold, loadedRun.FontWeight);
            Assert.Equal(FontStyles.Italic, loadedRun.FontStyle);
            Assert.Contains(loadedRun.TextDecorations,
                d => d.Location == TextDecorationLocation.Underline);
            Assert.Equal(html, FlowDocumentToHtml.Convert(loaded));
        });
    }

    [Fact]
    public void EditorPropertyFormatting_SaveAndReload()
    {
        Sta(() =>
        {
            var editor = new RichTextBox { Document = new FlowDocument(new Paragraph(new Run("styled"))) };
            editor.SelectAll();
            editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Strikethrough);
            editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.Red);
            editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Yellow);
            editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, 24d);

            string html = FlowDocumentToHtml.Convert(editor.Document);
            Assert.Equal("<p><s><span style=\"color:#FF0000; background-color:#FFFF00; font-size:24px\">styled</span></s></p>", html);

            var loaded = HtmlToFlowDocument.Convert(html);
            var loadedRun = Runs(loaded).Single(r => r.Text == "styled");
            Assert.Contains(loadedRun.TextDecorations,
                d => d.Location == TextDecorationLocation.Strikethrough);
            Assert.Equal(Colors.Red, ((SolidColorBrush)loadedRun.Foreground).Color);
            Assert.Equal(Colors.Yellow, ((SolidColorBrush)loadedRun.Background).Color);
            Assert.Equal(24, loadedRun.FontSize);
            Assert.Equal(html, FlowDocumentToHtml.Convert(loaded));
        });
    }

    [Fact]
    public void EditorSizePalette_AllChoicesRoundTrip()
    {
        Sta(() =>
        {
            foreach (double size in new[] { 11d, 14d, 18d, 24d })
            {
                var run = new Run("size") { FontSize = size };
                string html = FlowDocumentToHtml.Convert(new FlowDocument(new Paragraph(run)));
                var restored = Runs(HtmlToFlowDocument.Convert(html)).Single(r => r.Text == "size");
                Assert.Equal(size, restored.FontSize);
            }
        });
    }

    [Fact]
    public void EditorColorAndHighlightPalettes_AllChoicesRoundTrip()
    {
        Sta(() =>
        {
            string[] textColors = ["#111111", "#6B7280", "#DC2626", "#EA580C", "#16A34A", "#0891B2", "#2563EB", "#7C3AED"];
            string[] highlights = ["#FEF08A", "#BBF7D0", "#BFDBFE", "#FBCFE8", "#FED7AA", "#E9D5FF"];

            foreach (string hex in textColors)
            {
                var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
                string html = FlowDocumentToHtml.Convert(new FlowDocument(new Paragraph(new Run("color") { Foreground = brush })));
                var restored = Runs(HtmlToFlowDocument.Convert(html)).Single(r => r.Text == "color");
                Assert.Equal(brush.Color, ((SolidColorBrush)restored.Foreground).Color);
                Assert.Equal(html, FlowDocumentToHtml.Convert(HtmlToFlowDocument.Convert(html)));
            }

            foreach (string hex in highlights)
            {
                var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
                string html = FlowDocumentToHtml.Convert(new FlowDocument(new Paragraph(new Run("highlight") { Background = brush })));
                var restored = Runs(HtmlToFlowDocument.Convert(html)).Single(r => r.Text == "highlight");
                Assert.Equal(brush.Color, ((SolidColorBrush)restored.Background).Color);
                Assert.Equal(html, FlowDocumentToHtml.Convert(HtmlToFlowDocument.Convert(html)));
            }
        });
    }

    [Fact]
    public void EditorAlignmentAndLists_AllChoicesRoundTrip()
    {
        Sta(() =>
        {
            foreach (var alignment in new[] { TextAlignment.Left, TextAlignment.Center, TextAlignment.Right })
            {
                var editor = new RichTextBox { Document = new FlowDocument(new Paragraph(new Run("aligned"))) };
                editor.SelectAll();
                RoutedCommand command = alignment switch
                {
                    TextAlignment.Center => EditingCommands.AlignCenter,
                    TextAlignment.Right => EditingCommands.AlignRight,
                    _ => EditingCommands.AlignLeft,
                };
                command.Execute(null, editor);
                string html = FlowDocumentToHtml.Convert(editor.Document);
                Assert.Equal(alignment, HtmlToFlowDocument.Convert(html).Blocks.OfType<Paragraph>().Single().TextAlignment);
            }

            foreach (var (command, marker) in new[]
            {
                (EditingCommands.ToggleBullets, TextMarkerStyle.Disc),
                (EditingCommands.ToggleNumbering, TextMarkerStyle.Decimal),
            })
            {
                var editor = new RichTextBox { Document = new FlowDocument(new Paragraph(new Run("item"))) };
                editor.SelectAll();
                command.Execute(null, editor);
                string html = FlowDocumentToHtml.Convert(editor.Document);
                Assert.Equal(marker, HtmlToFlowDocument.Convert(html).Blocks.OfType<List>().Single().MarkerStyle);
            }
        });
    }

    [Theory]
    [InlineData("<p><b>formatted</b></p>", "bold")]
    [InlineData("<p><i>formatted</i></p>", "italic")]
    [InlineData("<p><u>formatted</u></p>", "underline")]
    public void EditorCommands_RemoveLoadedFormatting(string html, string commandName)
    {
        Sta(() =>
        {
            var editor = new RichTextBox { Document = HtmlToFlowDocument.Convert(html) };
            editor.SelectAll();
            RoutedCommand command = commandName switch
            {
                "bold" => EditingCommands.ToggleBold,
                "italic" => EditingCommands.ToggleItalic,
                _ => EditingCommands.ToggleUnderline,
            };
            command.Execute(null, editor);

            Assert.Equal("<p>formatted</p>", FlowDocumentToHtml.Convert(editor.Document));
        });
    }

    [Fact]
    public void EditorProperties_RemoveLoadedSizeColorHighlightAndAlignment()
    {
        Sta(() =>
        {
            const string html = "<p style=\"text-align:center\"><span style=\"color:#DC2626; background-color:#FEF08A; font-size:24px\">formatted</span></p>";
            var editor = new RichTextBox { Document = HtmlToFlowDocument.Convert(html) };
            editor.SelectAll();
            editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, FlowDocumentToHtml.NormalFontSize);
            EditorFormatting.UseDefaultForeground(editor.Selection, editor.Document.Foreground);
            EditorFormatting.RemoveHighlight(editor.Selection);
            EditingCommands.AlignLeft.Execute(null, editor);

            Assert.Equal("<p>formatted</p>", FlowDocumentToHtml.Convert(editor.Document));
        });
    }

    [Fact]
    public void EditorAutomaticColor_UsesHostDefaultWithoutPersistingIt()
    {
        Sta(() =>
        {
            var editor = new RichTextBox
            {
                Foreground = Brushes.White,
                Document = HtmlToFlowDocument.Convert("<p><span style=\"color:#DC2626\">formatted</span></p>"),
            };
            editor.SelectAll();
            EditorFormatting.UseDefaultForeground(editor.Selection, editor.Foreground);

            Assert.Equal(editor.Foreground, editor.Document.Foreground);
            Assert.Equal("<p>formatted</p>", FlowDocumentToHtml.Convert(editor.Document));
        });
    }

    [Fact]
    public void EditorCommands_ClearLoadedFormatting()
    {
        Sta(() =>
        {
            const string html = "<p style=\"text-align:right\"><b><i><u><s><span style=\"color:#DC2626; background-color:#FEF08A; font-size:24px\">formatted</span></s></u></i></b></p>";
            var editor = new RichTextBox { Document = HtmlToFlowDocument.Convert(html) };
            editor.SelectAll();
            EditorFormatting.Clear(editor.Selection);

            Assert.Equal("<p>formatted</p>", FlowDocumentToHtml.Convert(editor.Document));
        });
    }

    [Fact]
    public void EditorStrike_PreservesUnderline_AndTogglesIndependently()
    {
        Sta(() =>
        {
            var editor = new RichTextBox { Document = HtmlToFlowDocument.Convert("<p><u>formatted</u></p>") };
            editor.SelectAll();

            EditorFormatting.ToggleStrike(editor.Selection);
            Assert.Equal("<p><u><s>formatted</s></u></p>", FlowDocumentToHtml.Convert(editor.Document));

            editor.SelectAll();
            EditorFormatting.ToggleStrike(editor.Selection);
            Assert.Equal("<p><u>formatted</u></p>", FlowDocumentToHtml.Convert(editor.Document));
        });
    }

    [Fact]
    public void EditorStrike_RemovesLoadedFormatting()
    {
        Sta(() =>
        {
            var editor = new RichTextBox { Document = HtmlToFlowDocument.Convert("<p><s>formatted</s></p>") };
            editor.SelectAll();
            EditorFormatting.ToggleStrike(editor.Selection);

            Assert.Equal("<p>formatted</p>", FlowDocumentToHtml.Convert(editor.Document));
        });
    }

    [Fact]
    public void ListItemAlignment_SaveAndReload()
    {
        Sta(() =>
        {
            var list = new System.Windows.Documents.List { MarkerStyle = TextMarkerStyle.Disc };
            list.ListItems.Add(new ListItem(new Paragraph(new Run("centered"))
                { TextAlignment = TextAlignment.Center }));
            string html = FlowDocumentToHtml.Convert(new FlowDocument(list));

            Assert.Equal("<ul><li><p style=\"text-align:center\">centered</p></li></ul>", html);
            var restored = HtmlToFlowDocument.Convert(html).Blocks.OfType<System.Windows.Documents.List>().Single();
            Assert.Equal(TextAlignment.Center,
                restored.ListItems.Single().Blocks.OfType<Paragraph>().Single().TextAlignment);
        });
    }

    [Theory]
    [InlineData("<ul><li>item</li></ul>", "bullet")]
    [InlineData("<ol><li>item</li></ol>", "number")]
    public void EditorCommands_RemoveLoadedList(string html, string listType)
    {
        Sta(() =>
        {
            var editor = new RichTextBox { Document = HtmlToFlowDocument.Convert(html) };
            editor.SelectAll();
            (listType == "bullet" ? EditingCommands.ToggleBullets : EditingCommands.ToggleNumbering)
                .Execute(null, editor);

            Assert.Equal("<p>item</p>", FlowDocumentToHtml.Convert(editor.Document));
        });
    }

    [Fact]
    public void MixedFormatting_PartialSelection_SaveAndReload()
    {
        Sta(() =>
        {
            var editor = new RichTextBox { Document = new FlowDocument(new Paragraph(new Run("one two three"))) };
            var paragraph = editor.Document.Blocks.OfType<Paragraph>().Single();
            var run = paragraph.Inlines.OfType<Run>().Single();
            editor.Selection.Select(run.ContentStart.GetPositionAtOffset(4)!, run.ContentStart.GetPositionAtOffset(7)!);
            EditingCommands.ToggleBold.Execute(null, editor);
            editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.Red);

            string html = FlowDocumentToHtml.Convert(editor.Document);
            Assert.Contains("<b><span style=\"color:#FF0000\">two</span></b>", html);
            Assert.Equal(html, FlowDocumentToHtml.Convert(HtmlToFlowDocument.Convert(html)));
        });
    }

    [Fact]
    public void FormattedListAndLink_SaveAndReload()
    {
        Sta(() =>
        {
            const string html = "<ul><li><b><span style=\"color:#2563EB\">First</span></b></li><li><a href=\"https://example.com/\"><i>Second</i></a></li></ul>";
            string saved = FlowDocumentToHtml.Convert(HtmlToFlowDocument.Convert(html));
            Assert.Equal(html, saved);
            using var temp = new TempDir();
            ResponseFile.Save(new ResponseItem { Name = "List and link", Html = saved }, temp.Path);
            Assert.Equal(saved, ResponseFile.Load(temp.Of("List and link.json"), temp.Path).Html);
            var restored = HtmlToFlowDocument.Convert(saved);
            var list = restored.Blocks.OfType<System.Windows.Documents.List>().Single();
            Assert.Equal(FontWeights.Bold, Runs(list.ListItems.First().Blocks).Single().FontWeight);
            var link = list.ListItems.Last().Blocks.OfType<Paragraph>().Single().Inlines
                .OfType<Hyperlink>().Single();
            Assert.Equal(new Uri("https://example.com/"), link.NavigateUri);
            Assert.Equal(FontStyles.Italic, Runs(link.Inlines).Single().FontStyle);
        });
    }

    [Fact]
    public void EditorStrike_PreservesMixedUnderlineSelection()
    {
        Sta(() =>
        {
            var editor = new RichTextBox
            {
                Document = HtmlToFlowDocument.Convert("<p><u>underlined</u> plain</p>"),
            };
            editor.SelectAll();
            EditorFormatting.ToggleStrike(editor.Selection);
            Assert.Equal("<p><u><s>underlined</s></u><s> plain</s></p>",
                FlowDocumentToHtml.Convert(editor.Document));

            editor.SelectAll();
            EditorFormatting.ToggleStrike(editor.Selection);
            Assert.Equal("<p><u>underlined</u> plain</p>", FlowDocumentToHtml.Convert(editor.Document));
        });
    }

    [Fact]
    public void VariableTextFormatting_SaveAndReload()
    {
        Sta(() =>
        {
            var paragraph = new Paragraph(new Run("{{greeting}}"));
            var editor = new RichTextBox { Document = new FlowDocument(paragraph) };
            editor.SelectAll();
            EditingCommands.ToggleBold.Execute(null, editor);

            string html = FlowDocumentToHtml.Convert(editor.Document);
            Assert.Equal("<p><b>{{greeting}}</b></p>", html);
            var restored = Runs(HtmlToFlowDocument.Convert(html)).Single();
            Assert.Equal("{{greeting}}", restored.Text);
            Assert.Equal(FontWeights.Bold, restored.FontWeight);
        });
    }

    private static IEnumerable<Run> Runs(FlowDocument document)
    {
        foreach (var paragraph in document.Blocks.OfType<Paragraph>())
            foreach (var run in Runs(paragraph.Inlines))
                yield return run;
    }

    private static IEnumerable<Run> Runs(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run) yield return run;
            else if (inline is Span span)
                foreach (var child in Runs(span.Inlines)) yield return child;
        }
    }

    private static IEnumerable<Run> Runs(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph paragraph)
                foreach (var run in Runs(paragraph.Inlines)) yield return run;
            else if (block is System.Windows.Documents.List list)
                foreach (var item in list.ListItems)
                    foreach (var run in Runs(item.Blocks)) yield return run;
        }
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
