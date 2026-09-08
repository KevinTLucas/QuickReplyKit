using QuickReplyKit.Core.Model;

namespace QuickReplyKit.Services;

/// <summary>
/// The seam the launcher's action bar (Paste / Paste plain / Copy) calls into.
///
/// Phase 2 ships <see cref="ClipboardInsertService"/>, a stub that only puts content on the
/// Windows clipboard so the whole flow is exercisable. Phase 3 replaces the implementation with
/// the real engine described in ARCHITECTURE.md — capture the foreground window, set the
/// clipboard with the history-exclusion formats via raw Win32, SendInput Ctrl+V, then restore
/// the previous clipboard — WITHOUT changing this interface. Keep it stable.
/// </summary>
public interface IInsertService
{
    /// <summary>Insert the response as rich text (Unicode + HTML + optional RTF) into the target app.</summary>
    void InsertRich(ResponseItem item);

    /// <summary>Insert the response as plain text only.</summary>
    void InsertPlain(ResponseItem item);

    /// <summary>Explicit user copy to the normal clipboard (may appear in history; not restored).</summary>
    void Copy(ResponseItem item);
}
