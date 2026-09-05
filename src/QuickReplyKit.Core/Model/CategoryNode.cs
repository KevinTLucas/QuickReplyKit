namespace QuickReplyKit.Core.Model;

/// <summary>
/// A folder in the library. Empty folders are valid nodes (no children, no responses).
/// <see cref="Responses"/> includes invalid items so the UI can show error markers in place.
/// </summary>
public sealed class CategoryNode
{
    public string Name { get; set; } = "";

    /// <summary>Path relative to the library root, '/'-separated; "" for the root node.</summary>
    public string RelativePath { get; set; } = "";

    public List<CategoryNode> Children { get; } = new();

    /// <summary>Responses that live directly in this folder (valid and invalid).</summary>
    public List<ResponseItem> Responses { get; } = new();
}
