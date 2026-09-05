using QuickReplyKit.Core.Text;

namespace QuickReplyKit.Core.Model;

/// <summary>
/// One response, backed by a single .json file. Display name is the filename (never stored
/// inside the file) so copying/renaming files "just works". Invalid files still produce an
/// item (with <see cref="IsValid"/> false and <see cref="Error"/> set) so the UI can surface
/// the problem without a valid sibling being blocked.
/// </summary>
public sealed class ResponseItem
{
    /// <summary>Filename without extension.</summary>
    public string Name { get; set; } = "";

    /// <summary>Path relative to the library root, '/'-separated (e.g. "IXL/Licensing/No License.json").</summary>
    public string RelativePath { get; set; } = "";

    public string FullPath { get; set; } = "";

    /// <summary>Folder portion relative to root, '/'-joined; "" when at the root.</summary>
    public string CategoryPath { get; set; } = "";

    public string App { get; set; } = "";

    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();

    private string _html = "";
    private string? _plainText;

    /// <summary>Stored subset HTML.</summary>
    public string Html
    {
        get => _html;
        set { _html = value ?? ""; _plainText = null; }
    }

    /// <summary>Plain text derived from <see cref="Html"/> (cached).</summary>
    public string PlainText => _plainText ??= HtmlText.ToPlainText(_html);

    public bool Favorite { get; set; }

    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Modified { get; set; }

    public bool IsValid { get; set; } = true;

    /// <summary>Set when the file failed to parse; null when valid.</summary>
    public string? Error { get; set; }
}
