using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.Core.Text;
using QuickReplyKit.Interop;
using QuickReplyKit.Services;
using QuickReplyKit.ViewModels;

namespace QuickReplyKit;

/// <summary>
/// The <c>--selftest-paste</c> E2E gate: put a sentinel on the clipboard, launch an owned child
/// edit target, run a real <see cref="InsertService.InsertPlain"/> into it, read its text back, and confirm the
/// clipboard was restored. Prints exactly one line: <c>PASTE=&lt;ok|fail&gt; CLIPBOARD_RESTORED=&lt;ok|fail&gt;</c>.
/// When Windows denies a background test process foreground input, a direct test-only paste message keeps the real
/// clipboard/InsertService path deterministic. Cleanup closes the exact child process, so automated verification
/// never discovers, opens, or modifies a user's Notepad windows.
/// </summary>
internal static class SelfTest
{
    private const int WM_GETTEXT = 0x000D;
    private const int WM_GETTEXTLENGTH = 0x000E;
    private const int WM_PASTE = 0x0302;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_CHILD = 0x40000000;
    private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    private const uint WS_VSCROLL = 0x00200000;
    private const uint ES_MULTILINE = 0x0004;
    private const uint ES_AUTOVSCROLL = 0x0040;
    private const int SW_SHOW = 5;

    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int dwProcessId);
    private const int ATTACH_PARENT_PROCESS = -1;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public nint hwnd; public uint message; public nint wParam; public nint lParam; public uint time; public int ptX; public int ptY; }
    [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, nint hWnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref MSG msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(
        uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(
        nint parent, nint after, string className, string? windowName);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessage(
        nint window, int message, nint wParam, StringBuilder? lParam);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
    private const uint PM_REMOVE = 0x0001;

    // --- Raw clipboard readback (for --selftest-paste-rich) ------------------
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint hWndNewOwner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint uFormat);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string lpszFormat);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint hMem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint hMem);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(nint hMem);

    /// <summary>
    /// <c>--selftest-foreground</c>: prove <see cref="ForegroundMonitor"/> records the last real
    /// foreground window (the fix for paste targeting the wrong window on non-hotkey launches).
    /// Installs the monitor on this STA thread, focuses an owned child edit target, pumps messages so the
    /// OUTOFCONTEXT WinEvent is delivered, and checks the tracker captured that exact window.
    /// Prints one line: <c>FOREGROUND_TRACK=&lt;ok|fail&gt;</c>.
    /// </summary>
    public static int RunForeground()
    {
        AttachConsole(ATTACH_PARENT_PROCESS);
        bool ok = false;
        SelfTestTarget? target = null;
        try
        {
            // Install the monitor first, so the foreground change when the child target appears is caught.
            var tracker = new ForegroundTargetTracker();
            using var monitor = new ForegroundMonitor(tracker);

            target = StartTarget();

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 6000 && !ok)
            {
                while (PeekMessage(out MSG m, 0, 0, 0, PM_REMOVE)) { TranslateMessage(ref m); DispatchMessage(ref m); }

                // If the launch's foreground event was missed, force one the pre-installed hook will see.
                if (tracker.Target == 0) ForegroundWindow.Restore(target.Window);
                ok = tracker.Target == target.Window;
                Thread.Sleep(30);
            }
        }
        catch (Exception ex) { Console.Error.WriteLine("[selftest-fg] " + ex); }
        finally { target?.Dispose(); }

        Console.WriteLine($"FOREGROUND_TRACK={(ok ? "ok" : "fail")}");
        Console.Out.Flush();
        return ok ? 0 : 1;
    }

    /// <summary>
    /// <c>--selftest-paste-rich</c>: prove the rich paste path puts a well-formed CF_HTML payload on the
    /// clipboard — the formatting a rich target consumes — alongside the plain-text alternative. Runs the
    /// real <see cref="ClipboardWriter.WriteRich"/> + <see cref="CfHtml.Build"/> the paste engine uses,
    /// then reads the clipboard back with raw Win32 and asserts the "HTML Format" blob is byte-for-byte
    /// <see cref="CfHtml.Build"/> (valid header offsets + our bold fragment) and CF_UNICODETEXT is the
    /// plain text. Prints one line: <c>RICHPASTE=&lt;ok|fail&gt;</c>.
    ///
    /// NOTE: the preferred proof — pasting into an in-process RICHEDIT50W and reading the bold run back —
    /// is not achievable here: Msftedit has no HTML importer (EM_PASTESPECIAL of "HTML Format" pastes
    /// nothing; a plain WM_PASTE lands the CF_UNICODETEXT with no formatting), and EM_STREAMOUT's stream
    /// callback is rejected as an invalid indirect-call target (CFG fail-fast) for an in-proc control. So
    /// this asserts the clipboard payload, per the documented fallback.
    /// </summary>
    public static int RunRichPaste()
    {
        AttachConsole(ATTACH_PARENT_PROCESS);
        bool ok = false;
        try
        {
            string mark = "QRKRICH" + Guid.NewGuid().ToString("N")[..8];
            string html = $"<p>plain <b>{mark}</b> tail</p><ul><li>one</li><li>two</li></ul>";
            var item = new ResponseItem { Html = html };
            string expectedHtml = CfHtml.Build(html);
            string expectedRtf = HtmlToRtf.Convert(html);

            if (ClipboardWriter.WriteRich(item.PlainText, expectedHtml, expectedRtf))
            {
                uint rtfId = RegisterClipboardFormat("Rich Text Format");
                string gotHtml = ReadClipboardFormatUtf8(RegisterClipboardFormat("HTML Format"));
                string gotPlain = ReadClipboardText();
                string gotRtf = ReadClipboardFormatUtf8(rtfId);
                ok = gotHtml == expectedHtml
                     && gotHtml.Contains("StartFragment:", StringComparison.Ordinal)
                     && gotHtml.Contains(mark, StringComparison.Ordinal)
                     && gotPlain == item.PlainText
                     && gotRtf.Contains(@"\rtf1", StringComparison.Ordinal)
                     && gotRtf.Contains(@"\f0", StringComparison.Ordinal);
            }
        }
        catch (Exception ex) { Console.Error.WriteLine("[selftest-rich] " + ex); }

        Console.WriteLine($"RICHPASTE={(ok ? "ok" : "fail")}");
        Console.Out.Flush();
        return ok ? 0 : 1;
    }

    /// <summary>Read one clipboard format's HGLOBAL as a null-terminated UTF-8 string (raw Win32).</summary>
    private static string ReadClipboardFormatUtf8(uint format)
    {
        if (format == 0 || !OpenClipboard(0)) return "";
        try
        {
            nint h = GetClipboardData(format);
            if (h == 0) return "";
            nint p = GlobalLock(h);
            if (p == 0) return "";
            try
            {
                int size = (int)GlobalSize(h);
                if (size <= 0) return "";
                var bytes = new byte[size];
                Marshal.Copy(p, bytes, 0, size);
                int nul = Array.IndexOf(bytes, (byte)0);
                return Encoding.UTF8.GetString(bytes, 0, nul >= 0 ? nul : size);
            }
            finally { GlobalUnlock(h); }
        }
        finally { CloseClipboard(); }
    }

    /// <summary>
    /// <c>--selftest-paste-ui</c>: regression guard for "clicking Paste in the launcher does nothing".
    /// Drives the REAL command path — <see cref="LibraryService"/> + <see cref="LauncherViewModel"/> +
    /// <see cref="InsertService"/> wired as <c>App</c> wires them — from a seeded temp library into a
    /// fresh owned child edit target. The target is set explicitly (foreground CAPTURE is covered by
    /// <c>--selftest-foreground</c>); this proves the VM's default-insert command reaches the engine
    /// and text lands. The same test-only WM_PASTE fallback described on this class is used when the
    /// OS refuses foreground activation. Prints one line: <c>UIPASTE=&lt;ok|fail&gt;</c>.
    /// </summary>
    public static int RunUiPaste()
    {
        AttachConsole(ATTACH_PARENT_PROCESS);
        bool ok = false;
        SelfTestTarget? target = null;
        string? tempRoot = null;
        try
        {
            string pasted = "QRK_UIPASTE_" + Guid.NewGuid().ToString("N");
            // Include a {{day}} token so this also proves template resolution reaches the paste path.
            string day = DateTimeOffset.Now.ToString("dddd");

            tempRoot = Path.Combine(Path.GetTempPath(), "QRK_UITest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            File.WriteAllText(Path.Combine(tempRoot, "UI Test.json"),
                "{\"schema\":1,\"html\":\"<p>" + pasted + " {{day}}</p>\"}");
            string recentPath = Path.Combine(tempRoot, "recent.json");

            target = StartTarget();
            // Wire the same graph App builds; set the paste target explicitly (this test covers the
            // VM command → InsertService path, not the WinEvent foreground capture).
            using var library = new LibraryService(tempRoot, seedSourceDir: null, recentPath: recentPath, watch: false);
            var insert = SelfTestInsertService(target);
            var vm = new LauncherViewModel(library, insert, new AppConfig());

            vm.SelectedResult = vm.Results.FirstOrDefault();   // "click a response"
            if (vm.HasValidSelection) vm.TriggerDefaultInsert();   // the Enter / Paste command

            string got = target.WaitForText(pasted, 2000);
            // Both the marker and the RESOLVED {{day}} token must land (proves resolve-at-insert).
            ok = got.Contains(pasted, StringComparison.Ordinal)
                 && got.Contains(day, StringComparison.Ordinal);
        }
        catch (Exception ex) { Console.Error.WriteLine("[selftest-ui] " + ex); }
        finally
        {
            target?.Dispose();
            try { if (tempRoot != null) Directory.Delete(tempRoot, recursive: true); } catch { }
        }

        Console.WriteLine($"UIPASTE={(ok ? "ok" : "fail")}");
        Console.Out.Flush();
        return ok ? 0 : 1;
    }

    public static int Run()
    {
        AttachConsole(ATTACH_PARENT_PROCESS);

        bool pasteOk = false, restoredOk = false;
        SelfTestTarget? target = null;
        try
        {
            string sentinel = "QRK_CLIPBOARD_SENTINEL_" + Guid.NewGuid().ToString("N");
            string pasted = "QRK_PASTED_" + Guid.NewGuid().ToString("N");

            SetClipboardText(sentinel);

            target = StartTarget();
            var item = new ResponseItem { Html = "<p>" + pasted + "</p>" };
            var service = SelfTestInsertService(target);
            service.InsertPlain(item);

            // A freshly-generated GUID string that only reaches the target editor via our paste — its
            // presence in the control's text is unambiguous proof the Ctrl+V landed.
            pasteOk = target.WaitForText(pasted, 2000).Contains(pasted, StringComparison.Ordinal);
            restoredOk = ReadClipboardText() == sentinel;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[selftest] " + ex);
        }
        finally
        {
            target?.Dispose();
        }

        Print(pasteOk, restoredOk);
        return pasteOk && restoredOk ? 0 : 1;
    }

    private static void Print(bool paste, bool restored)
    {
        Console.WriteLine($"PASTE={(paste ? "ok" : "fail")} CLIPBOARD_RESTORED={(restored ? "ok" : "fail")}");
        Console.Out.Flush();
    }

    // --- clipboard (WPF, STA) ------------------------------------------------

    private static void SetClipboardText(string text)
    {
        for (int i = 0; i < 5; i++)
        {
            try { Clipboard.SetText(text); return; }
            catch { Thread.Sleep(30); }
        }
    }

    private static string ReadClipboardText()
    {
        for (int i = 0; i < 5; i++)
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
            catch { Thread.Sleep(30); }
        }
        return "";
    }

    // --- Owned child edit target ---------------------------------------------

    public static int RunTarget(string token)
    {
        if (!Guid.TryParseExact(token, "N", out _)) return 2;
        string title = TargetTitle(token);
        nint window = CreateWindowEx(0, "STATIC", title, WS_OVERLAPPEDWINDOW | WS_VISIBLE,
            100, 100, 360, 180, 0, 0, 0, 0);
        if (window == 0) return 2;
        nint edit = CreateWindowEx(0, "EDIT", "", WS_CHILD | WS_VISIBLE | WS_VSCROLL | ES_MULTILINE | ES_AUTOVSCROLL,
            8, 8, 328, 124, window, 0, 0, 0);
        if (edit == 0) return 2;
        ShowWindow(window, SW_SHOW);
        SetFocus(edit);
        while (IsWindow(window))
        {
            while (PeekMessage(out MSG message, 0, 0, 0, PM_REMOVE))
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
            Thread.Sleep(10);
        }
        return 0;
    }

    private static SelfTestTarget StartTarget()
    {
        string token = Guid.NewGuid().ToString("N");
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        start.ArgumentList.Add("--selftest-target");
        start.ArgumentList.Add(token);
        var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start self-test target.");
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 6000 && !process.HasExited)
        {
            process.Refresh();
            if (process.MainWindowHandle != 0)
            {
                nint edit = FindWindowEx(process.MainWindowHandle, 0, "EDIT", null);
                if (edit != 0) return new SelfTestTarget(process, process.MainWindowHandle, edit);
            }
            Thread.Sleep(50);
        }
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        process.Dispose();
        throw new TimeoutException("Self-test target did not open.");
    }

    private static string TargetTitle(string token) => "QuickReplyKit Self-Test Target " + token;

    private static InsertService SelfTestInsertService(SelfTestTarget target)
    {
        var tracker = new ForegroundTargetTracker { Target = target.Window };
        if (ForegroundWindow.Restore(target.Window)) return new InsertService(tracker);
        return new InsertService(tracker, () =>
        {
            SendMessage(target.Edit, WM_PASTE, 0, 0);
            return true;
        });
    }

    private sealed class SelfTestTarget(Process process, nint window, nint edit) : IDisposable
    {
        public nint Window { get; } = window;
        public nint Edit { get; } = edit;

        public string WaitForText(string expected, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            string text = "";
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                int length = (int)SendMessage(Edit, WM_GETTEXTLENGTH, 0, 0);
                if (length > 0)
                {
                    var buffer = new StringBuilder(length + 1);
                    SendMessage(Edit, WM_GETTEXT, length + 1, buffer);
                    text = buffer.ToString();
                }
                if (text.Contains(expected, StringComparison.Ordinal)) break;
                Thread.Sleep(20);
            }
            return text;
        }

        public void Dispose()
        {
            try
            {
                if (!process.HasExited) process.CloseMainWindow();
                if (!process.HasExited && !process.WaitForExit(3000)) process.Kill(entireProcessTree: true);
                if (!process.HasExited) process.WaitForExit(1000);
            }
            catch { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } }
            process.Dispose();
        }
    }

}
