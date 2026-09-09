using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using QuickReplyKit.Core.Diagnostics;

namespace QuickReplyKit.Interop;

/// <summary>Uses Word's own "keep source formatting" paste when Word is the captured target.</summary>
internal static class WordPaste
{
    private const int OriginalFormatting = 16;

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int CLSIDFromProgID(string progId, out Guid clsid);

    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int GetActiveObject(ref Guid clsid, nint reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object? instance);

    public static bool TryPasteKeepingSource(nint target)
    {
        if (!IsWord(target)) return false;
        if (CLSIDFromProgID("Word.Application", out Guid clsid) < 0)
        {
            Diag.Warn("WordPaste", "Paste", "Word target found but Word.Application is not registered");
            return false;
        }

        object? app = null;
        object? window = null;
        object? selection = null;
        try
        {
            if (GetActiveObject(ref clsid, 0, out app) < 0 || app == null)
            {
                Diag.Warn("WordPaste", "Paste", "Word target found but its COM application was unavailable");
                return false;
            }
            window = Get(app, "ActiveWindow");
            if (window == null || !SameWindow(target, Get(window, "Hwnd")))
            {
                Diag.Warn("WordPaste", "Paste", "Word active window did not match the captured paste target");
                return false;
            }
            selection = Get(app, "Selection");
            if (selection == null) return false;
            selection.GetType().InvokeMember("PasteAndFormat", BindingFlags.InvokeMethod,
                null, selection, [OriginalFormatting], CultureInfo.InvariantCulture);
            Diag.Verbose("WordPaste", "Paste", "Word keep-source paste complete");
            return true;
        }
        catch (Exception ex)
        {
            Diag.Warn("WordPaste", "Paste", "Word keep-source paste failed; falling back to Ctrl+V", ex);
            return false;
        }
        finally
        {
            Release(selection);
            Release(window);
            Release(app);
        }
    }

    private static bool IsWord(nint target)
    {
        if (target == 0 || GetWindowThreadProcessId(target, out uint processId) == 0) return false;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName.Equals("WINWORD", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static object? Get(object instance, string property) =>
        instance.GetType().InvokeMember(property, BindingFlags.GetProperty,
            null, instance, null, CultureInfo.InvariantCulture);

    private static bool SameWindow(nint expected, object? actual) => actual != null &&
        unchecked((uint)expected.ToInt64()) == unchecked((uint)Convert.ToInt64(actual, CultureInfo.InvariantCulture));

    private static void Release(object? instance)
    {
        if (instance != null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
    }
}
