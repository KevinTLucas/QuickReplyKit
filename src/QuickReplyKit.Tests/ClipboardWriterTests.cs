using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using QuickReplyKit.Core.Text;
using QuickReplyKit.Interop;

namespace QuickReplyKit.Tests;

/// <summary>
/// Proves the core Phase-3 promise WITHOUT needing Win+V: after <see cref="ClipboardWriter"/>
/// writes, open the clipboard and enumerate its formats to assert CF_UNICODETEXT (and "HTML Format"
/// for rich) is present alongside all three history/cloud-exclusion registered formats. Also
/// round-trips text through the raw-Win32 write (emoji, CJK, and a very large payload) to prove the
/// HGLOBAL copy is byte-exact and never truncates. All clipboard work runs on an STA thread.
/// </summary>
public class ClipboardWriterTests
{
    private const uint CF_UNICODETEXT = 13;

    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint hWnd);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern int CountClipboardFormats();
    [DllImport("user32.dll")] private static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string s);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint h);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint h);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(nint h);

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

    /// <summary>Open the clipboard (retrying briefly), run <paramref name="read"/>, always close.</summary>
    private static T WithClipboard<T>(Func<T> read)
    {
        for (int i = 0; i < 20 && !OpenClipboard(0); i++) Thread.Sleep(20);
        try { return read(); }
        finally { CloseClipboard(); }
    }

    private static HashSet<uint> EnumFormats()
    {
        var set = new HashSet<uint>();
        uint f = 0;
        while ((f = EnumClipboardFormats(f)) != 0) set.Add(f);
        return set;
    }

    private static byte[] ReadBytes(uint format)
    {
        nint h = GetClipboardData(format);
        if (h == 0) return Array.Empty<byte>();
        nint p = GlobalLock(h);
        if (p == 0) return Array.Empty<byte>();
        try
        {
            int size = (int)GlobalSize(h);
            var buf = new byte[size];
            Marshal.Copy(p, buf, 0, size);
            return buf;
        }
        finally { GlobalUnlock(h); }
    }

    private static void Clear()
    {
        for (int i = 0; i < 20 && !OpenClipboard(0); i++) Thread.Sleep(20);
        try { EmptyClipboard(); } finally { CloseClipboard(); }
    }

    [Fact]
    public void WritePlain_SetsUnicodeText_And_AllExclusionFormats() => Sta(() =>
    {
        try
        {
            Assert.True(ClipboardWriter.WritePlain("hello world"));

            var (formats, unicode) = WithClipboard(() => (EnumFormats(), ReadBytes(CF_UNICODETEXT)));

            Assert.Contains(CF_UNICODETEXT, formats);
            Assert.Contains(RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"), formats);
            Assert.Contains(RegisterClipboardFormat("CanIncludeInClipboardHistory"), formats);
            Assert.Contains(RegisterClipboardFormat("CanUploadToCloudClipboard"), formats);

            Assert.Equal("hello world", Encoding.Unicode.GetString(unicode).TrimEnd('\0'));
        }
        finally { Clear(); }
    });

    [Fact]
    public void WriteRich_AlsoSets_HtmlFormat_ByteExact() => Sta(() =>
    {
        try
        {
            string cf = CfHtml.Build("<p>Hello <b>world</b></p>");
            Assert.True(ClipboardWriter.WriteRich("Hello world", cf, @"{\rtf1\ansi\ansicpg1252\deff0\pard {\b Hello} world\par}"));

            uint htmlId = RegisterClipboardFormat("HTML Format");
            var (formats, htmlBytes) = WithClipboard(() => (EnumFormats(), ReadBytes(htmlId)));

            Assert.Contains(CF_UNICODETEXT, formats);
            Assert.Contains(htmlId, formats);
            Assert.Contains(RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"), formats);
            Assert.Contains(RegisterClipboardFormat("CanIncludeInClipboardHistory"), formats);
            Assert.Contains(RegisterClipboardFormat("CanUploadToCloudClipboard"), formats);

            // The writer appends a single NUL terminator; strip it and confirm byte-exactness.
            Assert.Equal(cf, Encoding.UTF8.GetString(htmlBytes).TrimEnd('\0'));
        }
        finally { Clear(); }
    });

    [Fact]
    public void WritePlain_Emoji_And_Cjk_RoundTrip() => Sta(() =>
    {
        try
        {
            string text = "café — naïve 日本語 😀🎉 end";
            Assert.True(ClipboardWriter.WritePlain(text));
            var unicode = WithClipboard(() => ReadBytes(CF_UNICODETEXT));
            Assert.Equal(text, Encoding.Unicode.GetString(unicode).TrimEnd('\0'));
        }
        finally { Clear(); }
    });

    [Fact]
    public void WritePlain_VeryLargeText_RoundTrips_NoTruncation() => Sta(() =>
    {
        try
        {
            string text = new string('X', 1_000_000) + "😀TAIL";
            Assert.True(ClipboardWriter.WritePlain(text));
            var unicode = WithClipboard(() => ReadBytes(CF_UNICODETEXT));
            string back = Encoding.Unicode.GetString(unicode).TrimEnd('\0');
            Assert.Equal(text.Length, back.Length);
            Assert.Equal(text, back);
        }
        finally { Clear(); }
    });

    [Fact]
    public void WriteRich_EmptyHtml_StillWritesExclusion() => Sta(() =>
    {
        try
        {
            // Degenerate but valid: empty fragment. Must not throw or leave the clipboard open.
            Assert.True(ClipboardWriter.WriteRich("", CfHtml.Build(""), @"{\rtf1\ansi\ansicpg1252\deff0\pard\par}"));
            var formats = WithClipboard(EnumFormats);
            Assert.Contains(RegisterClipboardFormat("CanIncludeInClipboardHistory"), formats);
        }
        finally { Clear(); }
    });

    [Fact]
    public void WriteRich_AlsoSets_RtfFormat_AndReturnsExpectedValue() => Sta(() =>
    {
        try
        {
            string rtf = @"{\rtf1\ansi\ansicpg1252\deff0\pard{\b hello} {\i world}\par\pard}";
            string cf = CfHtml.Build("<p><b>hello</b> <i>world</i></p>");
            Assert.True(ClipboardWriter.WriteRich("hello world", cf, rtf));

            uint rtfId = RegisterClipboardFormat("Rich Text Format");
            var (formats, rtfBytes) = WithClipboard(() => (EnumFormats(), ReadBytes(rtfId)));

            Assert.Contains(rtfId, formats);
            var got = Encoding.UTF8.GetString(rtfBytes).TrimEnd('\0');
            Assert.Equal(rtf, got);
        }
        finally { Clear(); }
    });
}
