namespace QuickReplyKit.Core.Model;

/// <summary>
/// An immutable-ish view of the library at scan time: the category tree plus a flattened
/// list of every response (valid and invalid).
/// </summary>
public sealed class LibrarySnapshot
{
    public CategoryNode Root { get; set; } = new();

    /// <summary>Every response found, valid and invalid, in scan order.</summary>
    public IReadOnlyList<ResponseItem> AllResponses { get; set; } = Array.Empty<ResponseItem>();

    public string LibraryRoot { get; set; } = "";

    public IEnumerable<ResponseItem> ValidResponses => AllResponses.Where(r => r.IsValid);

    public IEnumerable<ResponseItem> Favorites => ValidResponses.Where(r => r.Favorite);
}
