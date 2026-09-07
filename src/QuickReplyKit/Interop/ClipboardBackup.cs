using System.Collections.Specialized;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;

namespace QuickReplyKit.Interop;

/// <summary>
/// Snapshot the user's clipboard before we overwrite it, and restore it afterwards, so an insert
/// leaves the clipboard exactly as it was. Uses WPF <see cref="Clipboard"/> (reading formats is
/// all we need here; the history-excluded write is done by <see cref="ClipboardWriter"/>).
/// </summary>
internal static class ClipboardBackup
{
    private static readonly string[] TextFormats =
        { DataFormats.UnicodeText, DataFormats.Text, DataFormats.Html, DataFormats.Rtf };

    /// <summary>
    /// Copy the present formats into a detached <see cref="DataObject"/>. Returns null if the
    /// clipboard is empty or could not be read — <see cref="Restore"/> treats that as "clear ours".
    /// </summary>
    public static DataObject? Snapshot()
    {
        try
        {
            var src = Clipboard.GetDataObject();
            if (src is null) return null;

            var snap = new DataObject();
            bool any = false;

            foreach (var fmt in TextFormats)
            {
                if (src.GetDataPresent(fmt) && src.GetData(fmt) is { } data)
                {
                    snap.SetData(fmt, data);
                    any = true;
                }
            }

            if (src.GetDataPresent(DataFormats.Bitmap) && src.GetData(DataFormats.Bitmap) is BitmapSource bmp)
            {
                snap.SetImage(bmp);
                any = true;
            }

            if (src.GetDataPresent(DataFormats.FileDrop) && src.GetData(DataFormats.FileDrop) is string[] files)
            {
                var list = new StringCollection();
                list.AddRange(files);
                snap.SetFileDropList(list);
                any = true;
            }

            return any ? snap : null;
        }
        catch
        {
            return null;   // locked/odd clipboard: treat as nothing to restore
        }
    }

    /// <summary>Put the snapshot back; if there was nothing, clear our content so we leave nothing behind.</summary>
    public static bool Restore(DataObject? snapshot)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (snapshot is null) Clipboard.Clear();
                else Clipboard.SetDataObject(snapshot, copy: true);
                return true;
            }
            catch when (attempt < 4)
            {
                Thread.Sleep(30);
            }
            catch
            {
                return false;   // give up quietly; never crash the insert
            }
        }
        return false;
    }
}
