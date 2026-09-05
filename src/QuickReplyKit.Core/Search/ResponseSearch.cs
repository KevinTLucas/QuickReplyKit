using QuickReplyKit.Core.Model;

namespace QuickReplyKit.Core.Search;

/// <summary>
/// Case-insensitive, whitespace-tokenized search over responses. Every token must match
/// somewhere (AND). Each token scores by its best field — name-prefix &gt; name-contains &gt;
/// tag-exact &gt; tag-contains &gt; app &gt; category &gt; body — and results are ranked by the
/// summed token tiers (lower = better), then by name. Only valid responses are returned.
/// </summary>
public static class ResponseSearch
{
    // Lower tier = stronger match. NoMatch excludes the item for that token.
    private const int NamePrefix = 0;
    private const int NameContains = 1;
    private const int TagExact = 2;
    private const int TagContains = 3;
    private const int App = 4;
    private const int Category = 5;
    private const int Body = 6;
    private const int NoMatch = int.MaxValue;

    public static IReadOnlyList<ResponseItem> Search(IEnumerable<ResponseItem> items, string query)
    {
        var valid = items.Where(i => i.IsValid);

        if (string.IsNullOrWhiteSpace(query))
            return valid.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();

        string[] tokens = query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
            .ToArray();

        var scored = new List<(ResponseItem item, int score)>();
        foreach (var item in valid)
        {
            int total = 0;
            bool all = true;
            foreach (var token in tokens)
            {
                int tier = BestTier(item, token);
                if (tier == NoMatch) { all = false; break; }
                total += tier;
            }
            if (all) scored.Add((item, total));
        }

        return scored
            .OrderBy(s => s.score)
            .ThenBy(s => s.item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.item)
            .ToList();
    }

    private static int BestTier(ResponseItem item, string token)
    {
        string name = item.Name.ToLowerInvariant();
        if (name.StartsWith(token, StringComparison.Ordinal)) return NamePrefix;
        if (name.Contains(token, StringComparison.Ordinal)) return NameContains;

        foreach (var tag in item.Tags)
            if (tag.Equals(token, StringComparison.OrdinalIgnoreCase)) return TagExact;
        foreach (var tag in item.Tags)
            if (tag.Contains(token, StringComparison.OrdinalIgnoreCase)) return TagContains;

        if (item.App.Length > 0 && item.App.Contains(token, StringComparison.OrdinalIgnoreCase)) return App;
        if (item.CategoryPath.Length > 0 && item.CategoryPath.Contains(token, StringComparison.OrdinalIgnoreCase)) return Category;
        if (item.PlainText.Contains(token, StringComparison.OrdinalIgnoreCase)) return Body;

        return NoMatch;
    }
}
