using System.Diagnostics;
using System.IO;
using System.Windows.Documents;
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
/// The <c>--selftest-paste</c> E2E gate: put a sentinel on the clipboard, launch Notepad, run a
/// real <see cref="InsertService.InsertPlain"/> into it, read Notepad's text back, and confirm the
/// clipboard was restored. Prints exactly one line: <c>PASTE=&lt;ok|fail&gt; CLIPBOARD_RESTORED=&lt;ok|fail&gt;</c>.
/// Windows 11 ships the Store Notepad: we open one empty temporary document in a new window, poll
/// for its top-level "Notepad" window, and read its "RichEditD2DPT" child via WM_GETTEXT. When
/// Windows denies a background test process foreground input, a test-only WM_PASTE trigger keeps
/// the real clipboard/InsertService path deterministic. Cleanup undoes the paste and closes that
/// exact temporary window normally so Notepad session restore cannot accumulate QA tabs.
/// </summary>
internal static class SelfTest
{
    private const int WM_GETTEXT = 0x000D;
    private const int WM_GETTEXTLENGTH = 0x000E;
    private const int WM_CLOSE = 0x0010;
    private const int WM_PASTE = 0x0302;
    private const int WM_UNDO = 0x0304;

    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int dwProcessId);
    private const int ATTACH_PARENT_PROCESS = -1;

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, nint lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumWindowsProc cb, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hWnd, StringBuilder s, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessage(nint hWnd, int msg, nint wParam, StringBuilder? lParam);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hWnd, out uint pid);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public nint hwnd; public uint message; public nint wParam; public nint lParam; public uint time; public int ptX; public int ptY; }
    [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, nint hWnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref MSG msg);
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
    /// Installs the monitor on this STA thread, focuses a fresh Notepad, pumps messages so the
    /// OUTOFCONTEXT WinEvent is delivered, and checks the tracker captured Notepad's process.
    /// Prints one line: <c>FOREGROUND_TRACK=&lt;ok|fail&gt;</c>.
    /// </summary>
    public static int RunForeground()
    {
        AttachConsole(ATTACH_PARENT_PROCESS);
        bool ok = false;
        nint target = 0;
        string? notepadFile = null;
        try
        {
            // Install the monitor FIRST, so the foreground *change* when Notepad appears is caught.
            var tracker = new ForegroundTargetTracker();
            using var monitor = new ForegroundMonitor(tracker);

            var before = FindNotepadWindows();
            LaunchNotepad(out notepadFile);

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 6000 && !ok)
            {
                while (PeekMessage(out MSG m, 0, 0, 0, PM_REMOVE)) { TranslateMessage(ref m); DispatchMessage(ref m); }

                if (target == 0) target = WaitForNewNotepad(before, timeoutMs: 50);
                if (target != 0)
                {
                    // If the launch's foreground event was missed, force one the pre-installed hook will see.
                    if (tracker.Target == 0) ForegroundWindow.Restore(target);
                    if (tracker.Target != 0)
                    {
                        GetWindowThreadProcessId(target, out uint notepadPid);
                        GetWindowThreadProcessId(tracker.Target, out uint gotPid);
                        ok = notepadPid != 0 && gotPid == notepadPid;
                    }
                }
                Thread.Sleep(30);
            }
        }
        catch (Exception ex) { Console.Error.WriteLine("[selftest-fg] " + ex); }
        finally { CloseNotepad(target, notepadFile); }

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
            string expectedRtf = BuildRtf(html);

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

    private static string BuildRtf(string html)
    {
        var flow = HtmlToFlowDocument.Convert(html);
        using var stream = new MemoryStream();
        new TextRange(flow.ContentStart, flow.ContentEnd).Save(stream, DataFormats.Rtf);
        return Encoding.UTF8.GetString(stream.ToArray()).TrimEnd('\0');
    }

    /// <summary>
    /// <c>--selftest-paste-ui</c>: regression guard for "clicking Paste in the launcher does nothing".
    /// Drives the REAL command path — <see cref="LibraryService"/> + <see cref="LauncherViewModel"/> +
    /// <see cref="InsertService"/> wired as <c>App</c> wires them — from a seeded temp library into a
    /// fresh Notepad. The target is set explicitly (foreground CAPTURE is covered by
    /// <c>--selftest-foreground</c>); this proves the VM's default-insert command reaches the engine
    /// and text lands. The same test-only WM_PASTE fallback described on this class is used when the
    /// OS refuses foreground activation. Prints one line: <c>UIPASTE=&lt;ok|fail&gt;</c>.
    /// </summary>
    public static int RunUiPaste()
    {
        AttachConsole(ATTACH_PARENT_PROCESS);
        bool ok = false;
        nint target = 0;
        string? tempRoot = null;
        string? notepadFile = null;
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

            var before = FindNotepadWindows();
            LaunchNotepad(out notepadFile);
            target = WaitForNewNotepad(before, timeoutMs: 6000);
            if (target != 0)
            {
                Thread.Sleep(800);   // let the editor finish initializing

                // Wire the same graph App builds; set the paste target explicitly (this test covers the
                // VM command → InsertService path, not the WinEvent foreground capture).
                using var library = new LibraryService(tempRoot, seedSourceDir: null, recentPath: recentPath, watch: false);
                var insert = SelfTestInsertService(target);
                var vm = new LauncherViewModel(library, insert, new AppConfig());

                vm.SelectedResult = vm.Results.FirstOrDefault();   // "click a response"
                if (vm.HasValidSelection) vm.TriggerDefaultInsert();   // the Enter / Paste command

                Thread.Sleep(400);
                string got = ReadNotepadText(target);
                // Both the marker and the RESOLVED {{day}} token must land (proves resolve-at-insert).
                ok = got.Contains(pasted, StringComparison.Ordinal)
                     && got.Contains(day, StringComparison.Ordinal);
            }
        }
        catch (Exception ex) { Console.Error.WriteLine("[selftest-ui] " + ex); }
        finally
        {
            CloseNotepad(target, notepadFile);
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
        nint target = 0;
        string? notepadFile = null;
        try
        {
            string sentinel = "QRK_CLIPBOARD_SENTINEL_" + Guid.NewGuid().ToString("N");
            string pasted = "QRK_PASTED_" + Guid.NewGuid().ToString("N");

            SetClipboardText(sentinel);

            var before = FindNotepadWindows();
            LaunchNotepad(out notepadFile);
            target = WaitForNewNotepad(before, timeoutMs: 6000);
            if (target == 0) { Print(false, false); return 1; }

            Thread.Sleep(800);                 // let the editor finish initializing
            var item = new ResponseItem { Html = "<p>" + pasted + "</p>" };
            var service = SelfTestInsertService(target);
            service.InsertPlain(item);

            Thread.Sleep(400);
            // A freshly-generated GUID string that only reaches Notepad via our paste — its
            // presence in the control's text is unambiguous proof the Ctrl+V landed.
            pasteOk = ReadNotepadText(target).Contains(pasted, StringComparison.Ordinal);
            restoredOk = ReadClipboardText() == sentinel;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[selftest] " + ex);
        }
        finally
        {
            CloseNotepad(target, notepadFile);
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

    // --- Notepad window discovery / readback ---------------------------------

    private static HashSet<nint> FindNotepadWindows()
    {
        var set = new HashSet<nint>();
        EnumWindows((h, _) =>
        {
            if (IsWindowVisible(h) && ClassOf(h) == "Notepad") set.Add(h);
            return true;
        }, 0);
        return set;
    }

    private static nint WaitForNewNotepad(HashSet<nint> before, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            nint found = 0;
            EnumWindows((h, _) =>
            {
                if (found == 0 && IsWindowVisible(h) && ClassOf(h) == "Notepad" && !before.Contains(h)) found = h;
                return true;
            }, 0);
            if (found != 0) return found;
            Thread.Sleep(100);
        }
        return 0;
    }

    private static string ReadNotepadText(nint notepad)
    {
        // The edit control is a deep descendant (under DesktopChildSiteBridge/NotepadTextBox),
        // not a direct child, so recurse with EnumChildWindows rather than FindWindowEx.
        nint edit = FindNotepadEditor(notepad);
        if (edit == 0) return "";

        int len = (int)SendMessage(edit, WM_GETTEXTLENGTH, 0, 0);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        SendMessage(edit, WM_GETTEXT, len + 1, sb);
        return sb.ToString();
    }

    private static string ClassOf(nint h)
    {
        var sb = new StringBuilder(64);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    private static nint FindNotepadEditor(nint notepad)
    {
        nint edit = 0;
        EnumChildWindows(notepad, (h, _) =>
        {
            if (ClassOf(h) == "RichEditD2DPT") { edit = h; return false; }
            return true;
        }, 0);
        return edit;
    }

    private static void LaunchNotepad(out string file)
    {
        file = Path.Combine(Path.GetTempPath(), "QRK_Notepad_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(file, "");
        var start = new ProcessStartInfo(@"C:\Windows\System32\notepad.exe") { UseShellExecute = true };
        start.ArgumentList.Add("/newwindow");
        start.ArgumentList.Add(file);
        Process.Start(start)?.Dispose();
    }

    private static InsertService SelfTestInsertService(nint target)
    {
        nint edit = FindNotepadEditor(target);
        var tracker = new ForegroundTargetTracker { Target = target };
        if (ForegroundWindow.Restore(target) || edit == 0) return new InsertService(tracker);
        return new InsertService(tracker, () =>
        {
            SendMessage(edit, WM_PASTE, 0, 0);
            return true;
        });
    }

    private static void CloseNotepad(nint target, string? file)
    {
        try
        {
            if (target != 0 && IsWindow(target))
            {
                nint edit = FindNotepadEditor(target);
                if (edit != 0) SendMessage(edit, WM_UNDO, 0, 0);
                SendMessage(target, WM_CLOSE, 0, 0);
                for (int i = 0; i < 20 && IsWindow(target); i++) Thread.Sleep(50);
            }
        }
        catch { }
        finally
        {
            try { if (file != null) File.Delete(file); } catch { }
        }
    }

}
