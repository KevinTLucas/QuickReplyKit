using QuickReplyKit.Core.Model;

namespace QuickReplyKit.Services;

/// <summary>
/// The seam the launcher's action bar (Paste / Paste plain / Copy) calls into.
/// The implementation captures the foreground target, publishes history-excluded clipboard data,
/// inserts it, and restores the previous clipboard. Keep this interface stable.
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
