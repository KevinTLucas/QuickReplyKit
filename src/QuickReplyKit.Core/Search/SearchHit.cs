using QuickReplyKit.Core.Model;

namespace QuickReplyKit.Core.Search;

/// <summary>A half-open character range <c>[Start, Start+Length)</c> within a response name.</summary>
public readonly record struct MatchSpan(int Start, int Length);

/// <summary>
/// One ranked search result plus the context that explains it: the spans of the name that the
/// query hit (for highlighting) and, when the match was body-only, a short single-line snippet
/// of the matching text.
/// </summary>
public sealed record SearchHit(ResponseItem Item, IReadOnlyList<MatchSpan> NameSpans, string? Snippet);
