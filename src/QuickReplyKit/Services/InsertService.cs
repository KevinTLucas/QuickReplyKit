using System.Diagnostics;
using System.Threading;
using System.Windows;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Text;
using QuickReplyKit.Interop;

namespace QuickReplyKit.Services;

/// <summary>
/// The real Phase-3 paste engine behind <see cref="IInsertService"/>. Runs the clipboard-safe
/// insert sequence: snapshot the clipboard, restore focus to the captured target, write the
/// response with history exclusion (raw Win32), SendInput Ctrl+V, then restore the clipboard.
/// The ViewModel owns mode/hide/recent; this service only does the injection.
/// </summary>
public sealed class InsertService : IInsertService
{
    private readonly ForegroundTargetTracker _tracker;

    /// <summary>Time to let the target consume Ctrl+V before we put the user's clipboard back.</summary>
    private const int PasteConsumeMs = 120;

    private bool _busy;   // guards against a double-insert during the ~300 ms sequence

    public InsertService(ForegroundTargetTracker tracker) => _tracker = tracker;

    public void InsertRich(ResponseItem item) => Insert(item, rich: true);

    public void InsertPlain(ResponseItem item) => Insert(item, rich: false);

    /// <summary>User-intended copy: normal clipboard set, no exclusion, no restore.</summary>
    public void Copy(ResponseItem item)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, item.PlainText);
        data.SetData(DataFormats.Html, CfHtml.Build(item.Html));
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try { Clipboard.SetDataObject(data, copy: true); return; }
            catch when (attempt < 2) { Thread.Sleep(30); }
            catch (Exception ex) { Debug.WriteLine("[QuickReplyKit] copy failed: " + ex.Message); return; }
        }
    }

    private void Insert(ResponseItem item, bool rich)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            nint target = _tracker.Target;

            // Target gone (app closed between open and insert): leave the text on the clipboard,
            // no exclusion, no restore — do not throw.
            if (!ForegroundWindow.IsAlive(target))
            {
                Copy(item);
                return;
            }

            var snapshot = ClipboardBackup.Snapshot();

            ForegroundWindow.Restore(target);

            bool wrote = rich
                ? ClipboardWriter.WriteRich(item.PlainText, CfHtml.Build(item.Html))
                : ClipboardWriter.WritePlain(item.PlainText);

            if (!wrote) { ClipboardBackup.Restore(snapshot); return; }

            InputSender.SendCtrlV();
            Thread.Sleep(PasteConsumeMs);

            ClipboardBackup.Restore(snapshot);
        }
        finally
        {
            _busy = false;
        }
    }
}
