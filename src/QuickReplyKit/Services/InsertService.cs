using System.Diagnostics;
using System.Threading;
using System.Windows;
using QuickReplyKit.Core.Diagnostics;
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
    private readonly Func<bool> _sendPaste;

    /// <summary>Time to let the target consume Ctrl+V before we put the user's clipboard back.</summary>
    private const int PasteConsumeMs = 120;

    private bool _busy;   // guards against a double-insert during the ~300 ms sequence

    public InsertService(ForegroundTargetTracker tracker) : this(tracker, InputSender.SendCtrlV) { }

    internal InsertService(ForegroundTargetTracker tracker, Func<bool> sendPaste)
    {
        _tracker = tracker;
        _sendPaste = sendPaste;
    }

    public void InsertRich(ResponseItem item) => Insert(item, rich: true);

    public void InsertPlain(ResponseItem item) => Insert(item, rich: false);

    /// <summary>User-intended copy: normal clipboard set, no exclusion, no restore.</summary>
    public void Copy(ResponseItem item) => TryCopy(item);

    private static bool TryCopy(ResponseItem item)
    {
        string rtf = HtmlToRtf.Convert(item.Html);
        var data = new DataObject();
        if (!string.IsNullOrWhiteSpace(rtf)) data.SetData(DataFormats.Rtf, rtf);
        data.SetData(DataFormats.Html, CfHtml.Build(item.Html));
        data.SetData(DataFormats.UnicodeText, item.PlainText);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                Diag.Verbose("InsertService", "Action", $"copy complete textLength={item.PlainText.Length} htmlLength={item.Html.Length}");
                return true;
            }
            catch when (attempt < 2) { Thread.Sleep(30); }
            catch (Exception ex)
            {
                Debug.WriteLine("[QuickReplyKit] copy failed");
                Diag.Warn("InsertService", "Action", "copy failed", ex);
                return false;
            }
        }
        return false;
    }

    private void Insert(ResponseItem item, bool rich)
    {
        if (_busy)
        {
            Diag.Verbose("InsertService", "Paste", "completion result=ignored reason=busy");
            return;
        }
        _busy = true;
        try
        {
            nint target = _tracker.Target;
            bool targetPresent = ForegroundWindow.IsAlive(target);
            Diag.Verbose("InsertService", "Paste", $"target present={targetPresent}");

            // Target gone (app closed between open and insert): leave the text on the clipboard,
            // no exclusion, no restore — do not throw.
            if (!targetPresent)
            {
                bool copied = TryCopy(item);
                Diag.Info("InsertService", "Paste", $"completion result=fallback fallback=copy success={copied}");
                return;
            }

            var snapshot = ClipboardBackup.Snapshot();
            Diag.Verbose("InsertService", "Paste", $"snapshot captured={snapshot is not null}");

            Diag.Verbose("InsertService", "Paste", "focus attempt=true");
            bool focused = ForegroundWindow.Restore(target);
            Diag.Verbose("InsertService", "Paste", $"focus result={focused}");

            var html = CfHtml.Build(item.Html);
            var rtf = HtmlToRtf.Convert(item.Html);
            bool wrote = rich
                ? ClipboardWriter.WriteRich(item.PlainText, html, rtf)
                : ClipboardWriter.WritePlain(item.PlainText);
            Diag.Verbose("InsertService", "Paste",
                $"clipboard transientWrite={wrote} mode={(rich ? "rich" : "plain")} textLength={item.PlainText.Length} htmlLength={(rich ? item.Html.Length : 0)} rtfLength={(rich ? rtf?.Length ?? 0 : 0)}");

            if (!wrote)
            {
                bool restoredAfterFailure = ClipboardBackup.Restore(snapshot);
                Diag.Verbose("InsertService", "Paste", $"clipboard restore={restoredAfterFailure}");
                Diag.Warn("InsertService", "Paste", "completion result=failed stage=clipboardWrite");
                return;
            }

            bool sent = _sendPaste();
            Diag.Verbose("InsertService", "Paste", $"input send={sent}");
            Thread.Sleep(PasteConsumeMs);

            bool restored = ClipboardBackup.Restore(snapshot);
            Diag.Verbose("InsertService", "Paste", $"clipboard restore={restored}");
            Diag.Info("InsertService", "Paste", $"completion result=complete inputSent={sent} clipboardRestored={restored}");
        }
        finally
        {
            _busy = false;
        }
    }

}
