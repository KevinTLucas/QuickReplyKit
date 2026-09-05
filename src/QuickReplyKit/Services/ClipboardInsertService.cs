using System.Diagnostics;
using System.Windows;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Text;

namespace QuickReplyKit.Services;

/// <summary>
/// Phase-2 stub for <see cref="IInsertService"/>: it only writes to the Windows clipboard (and
/// logs), so the launcher's buttons and the Enter/Shift+Enter keyboard flow do something real
/// and testable. It does NOT restore the clipboard, exclude from history, or paste into the
/// target app — that is Phase 3's SendInput engine. "Rich" here just means CF_HTML + text.
/// </summary>
public sealed class ClipboardInsertService : IInsertService
{
    public void InsertRich(ResponseItem item)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, item.PlainText);
        data.SetData(DataFormats.Html, CfHtml.Build(item.Html));
        SetClipboard(data, $"InsertRich (stub) → clipboard: {item.Name}");
    }

    public void InsertPlain(ResponseItem item)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, item.PlainText);
        SetClipboard(data, $"InsertPlain (stub) → clipboard: {item.Name}");
    }

    public void Copy(ResponseItem item)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, item.PlainText);
        data.SetData(DataFormats.Html, CfHtml.Build(item.Html));
        SetClipboard(data, $"Copy → clipboard: {item.Name}");
    }

    private static void SetClipboard(DataObject data, string log)
    {
        // Clipboard.SetDataObject occasionally throws (another process holds the clipboard open);
        // a couple of retries is the standard mitigation. Failure must not crash the launcher.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                Debug.WriteLine("[QuickReplyKit] " + log);
                return;
            }
            catch (Exception ex) when (attempt < 2)
            {
                Debug.WriteLine("[QuickReplyKit] clipboard busy, retrying: " + ex.Message);
                System.Threading.Thread.Sleep(30);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[QuickReplyKit] clipboard set failed: " + ex.Message);
            }
        }
    }
}
