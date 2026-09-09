using System.Threading;
using System.Windows;
using System.Windows.Interop;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Interop;
using QuickReplyKit.Services;

namespace QuickReplyKit.Tests;

/// <summary>
/// Behavioural tests for <see cref="InsertService"/> that don't need a live target window:
/// the target-gone fallback (leave text on the clipboard, no throw) and the user-intended Copy.
/// The full clipboard-safe insert sequence is exercised by the <c>--selftest-paste</c> E2E gate;
/// it uses native Ctrl+V when Windows grants foreground input and a direct test-only child-window paste
/// trigger when a background automation host is correctly denied foreground access.
/// All bodies run on an STA thread because they touch WPF <see cref="Clipboard"/>.
/// </summary>
[Collection(ClipboardCollection.Name)]
public class InsertServiceTests
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

    private static ResponseItem Item(string html) => new() { Html = html };

    [Fact]
    public void InsertPlain_TargetGone_FallsBackToClipboard_NoThrow() => Sta(() =>
    {
        // Target = 0 is never a live window, so IsAlive is false and Insert must take the
        // "app was closed" fallback: leave the text on the clipboard and return without throwing.
        var svc = new InsertService(new ForegroundTargetTracker { Target = 0 });
        var item = Item("<p>fallback text</p>");

        svc.InsertPlain(item);   // must not throw

        Assert.True(Clipboard.ContainsText());
        Assert.Equal("fallback text", Clipboard.GetText());
        Clipboard.Clear();
    });

    [Fact]
    public void InsertRich_TargetGone_FallsBackToClipboard_NoThrow() => Sta(() =>
    {
        var svc = new InsertService(new ForegroundTargetTracker { Target = 0 });
        svc.InsertRich(Item("<p>rich fallback</p>"));   // must not throw

        Assert.True(Clipboard.ContainsText());
        Assert.Equal("rich fallback", Clipboard.GetText());
        Assert.True(Clipboard.ContainsData(DataFormats.Html));
        Assert.True(Clipboard.ContainsData(DataFormats.Rtf));
        Clipboard.Clear();
    });

    [Fact]
    public void Copy_SetsBothTextAndHtml_NoException() => Sta(() =>
    {
        var svc = new InsertService(new ForegroundTargetTracker());
        svc.Copy(Item("<p>copy me</p>"));

        Assert.Equal("copy me", Clipboard.GetText());
        Assert.True(Clipboard.ContainsData(DataFormats.Html));
        Assert.True(Clipboard.ContainsData(DataFormats.Rtf));
        string[] formats = Clipboard.GetDataObject()!.GetFormats(autoConvert: false);
        Assert.True(Array.IndexOf(formats, DataFormats.Rtf) < Array.IndexOf(formats, DataFormats.Html));
        Assert.True(Array.IndexOf(formats, DataFormats.Html) < Array.IndexOf(formats, DataFormats.UnicodeText));
        Clipboard.Clear();
    });

    [Fact]
    public void InsertRich_UsesTargetSpecificKeepSourcePasteBeforeGenericInput() => Sta(() =>
    {
        using var target = TestWindow();
        bool keepSourceCalled = false;
        bool genericCalled = false;
        Clipboard.SetText("original clipboard");
        var svc = new InsertService(new ForegroundTargetTracker { Target = target.Handle },
            () => { genericCalled = true; return true; },
            hwnd => { keepSourceCalled = hwnd == target.Handle; return true; });

        svc.InsertRich(Item("<p><b>formatted</b></p>"));

        Assert.True(keepSourceCalled);
        Assert.False(genericCalled);
        Assert.Equal("original clipboard", Clipboard.GetText());
        Clipboard.Clear();
    });

    [Fact]
    public void InsertRich_FallsBackToGenericInputWhenKeepSourceIsUnavailable() => Sta(() =>
    {
        using var target = TestWindow();
        bool genericCalled = false;
        var svc = new InsertService(new ForegroundTargetTracker { Target = target.Handle },
            () => { genericCalled = true; return true; }, _ => false);

        svc.InsertRich(Item("<p>formatted</p>"));

        Assert.True(genericCalled);
        Clipboard.Clear();
    });

    [Fact]
    public void InsertPlain_NeverUsesTargetSpecificRichPaste() => Sta(() =>
    {
        using var target = TestWindow();
        bool keepSourceCalled = false;
        bool genericCalled = false;
        var svc = new InsertService(new ForegroundTargetTracker { Target = target.Handle },
            () => { genericCalled = true; return true; },
            _ => { keepSourceCalled = true; return true; });

        svc.InsertPlain(Item("<p>plain</p>"));

        Assert.False(keepSourceCalled);
        Assert.True(genericCalled);
        Clipboard.Clear();
    });

    private static HwndSource TestWindow() => new(new HwndSourceParameters("QuickReplyKit.InsertServiceTests")
    {
        Width = 1,
        Height = 1,
        WindowStyle = unchecked((int)0x80000000),
    });
}
