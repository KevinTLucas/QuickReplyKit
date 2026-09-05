using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace QuickReplyKit.Interop;

/// <summary>
/// Writes the response onto the clipboard with raw Win32 so we can set the history/cloud
/// exclusion formats WPF cannot. Always closes the clipboard on every path; hands each HGLOBAL
/// to the clipboard only on success (the OS then owns it), otherwise frees it.
/// </summary>
internal static class ClipboardWriter
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint hWndNewOwner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern nint SetClipboardData(uint uFormat, nint hMem);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint uFlags, nuint dwBytes);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint hMem);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint hMem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint hMem);

    /// <summary>Plain text only (+ exclusion formats).</summary>
    public static bool WritePlain(string text) => Write(text, html: null);

    /// <summary>CF_UNICODETEXT + CF_HTML (+ exclusion formats).</summary>
    public static bool WriteRich(string text, string cfHtml) => Write(text, cfHtml);

    private static bool Write(string text, string? html)
    {
        if (!Open()) return false;
        try
        {
            EmptyClipboard();

            if (!SetUnicode(text)) return false;
            if (html is not null && !SetBytes(RegisterClipboardFormat("HTML Format"), Encoding.UTF8.GetBytes(html + "\0")))
                return false;

            // History / cloud exclusion — a zero DWORD payload for each registered format.
            SetExclusion("ExcludeClipboardContentFromMonitorProcessing");
            SetExclusion("CanIncludeInClipboardHistory");
            SetExclusion("CanUploadToCloudClipboard");
            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool SetUnicode(string text)
    {
        var bytes = Encoding.Unicode.GetBytes(text + "\0");   // UTF-16LE, null-terminated
        return SetBytes(CF_UNICODETEXT, bytes);
    }

    private static void SetExclusion(string format)
    {
        uint id = RegisterClipboardFormat(format);
        if (id != 0) SetBytes(id, new byte[4]);   // DWORD 0
    }

    /// <summary>Copy <paramref name="bytes"/> into a moveable HGLOBAL and hand it to the clipboard.</summary>
    private static bool SetBytes(uint format, byte[] bytes)
    {
        if (format == 0) return false;
        nint h = GlobalAlloc(GMEM_MOVEABLE, (nuint)bytes.Length);
        if (h == 0) return false;

        nint p = GlobalLock(h);
        if (p == 0) { GlobalFree(h); return false; }
        try { Marshal.Copy(bytes, 0, p, bytes.Length); }
        finally { GlobalUnlock(h); }

        if (SetClipboardData(format, h) == 0) { GlobalFree(h); return false; }   // OS now owns h
        return true;
    }

    private static bool Open()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(0)) return true;
            Thread.Sleep(20);
        }
        return false;
    }
}
