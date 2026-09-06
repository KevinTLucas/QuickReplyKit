using System.Text.RegularExpressions;
using QuickReplyKit.Core.Model;

namespace QuickReplyKit.Core.Search;

/// <summary>
/// Case-insensitive, whitespace-tokenized search over responses. Every token must match
/// somewhere (AND). Each token scores by its best field — name-prefix &gt; name-contains &gt;
/// tag-exact &gt; tag-contains &gt; app &gt; category &gt; body — and results are ranked by the
/// summed token tiers (lower = better), then by name.
///
/// Ranking refinements: valid responses rank above invalid ones (invalid files are matched by
/// <b>name only</b> so a broken response can still be found and fixed, though the UI keeps them
/// non-pasteable); when a <paramref name="scope"/> category is given, in-scope hits rank first.
/// When a token has <b>zero</b> exact matches anywhere, a bounded, dependency-free fuzzy fallback
/// (subsequence / small edit distance on name+tags) runs for that token at a low tier — computed
/// only when needed, so the common has-exact-matches path is untouched.
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
    private const int Fuzzy = 10;   // below every exact tier
    private const int NoMatch = int.MaxValue;

    private static readonly IReadOnlyList<MatchSpan> NoSpans = Array.Empty<MatchSpan>();

    /// <summary>Ranked search returning just the items (name/spans metadata dropped).</summary>
    public static IReadOnlyList<ResponseItem> Search(IEnumerable<ResponseItem> items, string query, string? scope = null)
        => Rank(items, query, scope).Select(h => h.Item).ToList();

    /// <summary>Ranked search returning each hit with its name-match spans and optional body snippet.</summary>
    public static IReadOnlyList<SearchHit> Rank(IEnumerable<ResponseItem> items, string query, string? scope = null)
    {
        var list = items as IReadOnlyList<ResponseItem> ?? items.ToList();

        if (string.IsNullOrWhiteSpace(query))
            return list.Where(i => i.IsValid)
                       .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                       .Select(i => new SearchHit(i, NoSpans, null))
                       .ToList();

        string[] tokens = query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
            .ToArray();

        // Exact pass. Also learns, per token, whether it matched anywhere (drives lazy fuzzy).
        var tokenHasExact = new bool[tokens.Length];
        var scored = ScorePass(list, tokens, fuzzy: null, scope, tokenHasExact);

        // Lazy fuzzy fallback: only for tokens that got zero exact matches — and only then re-score.
        bool[]? fuzzy = null;
        for (int t = 0; t < tokens.Length; t++)
            if (!tokenHasExact[t]) (fuzzy ??= new bool[tokens.Length])[t] = true;
        if (fuzzy != null)
            scored = ScorePass(list, tokens, fuzzy, scope, tokenHasExact: null);

        return scored
            .OrderBy(s => s.Item.IsValid ? 0 : 1)   // valid before invalid
            .ThenBy(s => s.ScopeGroup)              // in-scope before the rest
            .ThenBy(s => s.Total)                   // stronger field match
            .ThenBy(s => s.Item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => new SearchHit(s.Item, s.NameSpans, s.Snippet))
            .ToList();
    }

    private readonly record struct Scored(
        ResponseItem Item, int Total, int ScopeGroup, IReadOnlyList<MatchSpan> NameSpans, string? Snippet);

    private static List<Scored> ScorePass(
        IReadOnlyList<ResponseItem> list, string[] tokens, bool[]? fuzzy, string? scope, bool[]? tokenHasExact)
    {
        bool scoped = !string.IsNullOrEmpty(scope);
        var result = new List<Scored>();

        foreach (var item in list)
        {
            string name = item.Name.ToLowerInvariant();
            int total = 0;
            bool all = true;
            string? bodyToken = null;

            for (int t = 0; t < tokens.Length; t++)
            {
                bool allowFuzzy = fuzzy != null && fuzzy[t];
                int tier = Tier(item, name, tokens[t], allowFuzzy);
                // Only exact tiers count toward "has an exact match" — evaluate every token
                // (no early break) so the flag is complete across all items.
                if (tier != NoMatch && tier < Fuzzy && tokenHasExact != null) tokenHasExact[t] = true;
                if (tier == NoMatch) { all = false; continue; }
                total += tier;
                if (tier == Body && bodyToken == null) bodyToken = tokens[t];
            }
            if (!all) continue;

            var spans = NameSpansFor(item.Name, name, tokens);
            string? snippet = spans.Count == 0 && bodyToken != null ? SnippetFor(item.PlainText, bodyToken) : null;
            int scopeGroup = scoped && InScope(item, scope!) ? 0 : 1;
            result.Add(new Scored(item, total, scopeGroup, spans, snippet));
        }
        return result;
    }

    private static int Tier(ResponseItem item, string lowerName, string token, bool allowFuzzy)
    {
        if (lowerName.StartsWith(token, StringComparison.Ordinal)) return NamePrefix;
        if (lowerName.Contains(token, StringComparison.Ordinal)) return NameContains;

        // Invalid files are searchable by name only (still surfaced, kept non-pasteable by the UI).
        if (!item.IsValid)
            return allowFuzzy && Subsequence(lowerName, token) ? Fuzzy : NoMatch;

        foreach (var tag in item.Tags)
            if (tag.Equals(token, StringComparison.OrdinalIgnoreCase)) return TagExact;
        foreach (var tag in item.Tags)
            if (tag.Contains(token, StringComparison.OrdinalIgnoreCase)) return TagContains;

        if (item.App.Length > 0 && item.App.Contains(token, StringComparison.OrdinalIgnoreCase)) return App;
        if (item.CategoryPath.Length > 0 && item.CategoryPath.Contains(token, StringComparison.OrdinalIgnoreCase)) return Category;
        if (item.PlainText.Contains(token, StringComparison.OrdinalIgnoreCase)) return Body;

        if (allowFuzzy && FuzzyMatch(item, lowerName, token)) return Fuzzy;
        return NoMatch;
    }

    private static bool InScope(ResponseItem item, string scope) =>
        item.CategoryPath.Equals(scope, StringComparison.OrdinalIgnoreCase) ||
        item.CategoryPath.StartsWith(scope + "/", StringComparison.OrdinalIgnoreCase);

    // --- Highlight spans ----------------------------------------------------

    /// <summary>Merged, sorted spans where <paramref name="tokens"/> occur literally in the name.</summary>
    private static IReadOnlyList<MatchSpan> NameSpansFor(string original, string lower, string[] tokens)
    {
        List<MatchSpan>? spans = null;
        foreach (var token in tokens)
        {
            int from = 0, idx;
            while ((idx = lower.IndexOf(token, from, StringComparison.Ordinal)) >= 0)
            {
                (spans ??= new List<MatchSpan>()).Add(new MatchSpan(idx, token.Length));
                from = idx + token.Length;
            }
        }
        if (spans == null) return NoSpans;

        spans.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        var merged = new List<MatchSpan>(spans.Count);
        foreach (var s in spans)
        {
            if (merged.Count > 0)
            {
                var last = merged[^1];
                if (s.Start <= last.Start + last.Length)   // overlapping or adjacent → merge
                {
                    int end = Math.Max(last.Start + last.Length, s.Start + s.Length);
                    merged[^1] = new MatchSpan(last.Start, end - last.Start);
                    continue;
                }
            }
            merged.Add(s);
        }
        return merged;
    }

    // --- Body snippet -------------------------------------------------------

    private static string SnippetFor(string plain, string token)
    {
        int idx = plain.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";
        const int pad = 32;
        int start = Math.Max(0, idx - pad);
        int end = Math.Min(plain.Length, idx + token.Length + pad);
        string ctx = Regex.Replace(plain[start..end], @"\s+", " ").Trim();
        if (start > 0) ctx = "… " + ctx;
        if (end < plain.Length) ctx += " …";
        return ctx;
    }

    // --- Bounded, dependency-free fuzzy matching ----------------------------

    private static bool FuzzyMatch(ResponseItem item, string lowerName, string token)
    {
        if (FuzzyField(lowerName, token)) return true;
        foreach (var tag in item.Tags)
            if (FuzzyField(tag.ToLowerInvariant(), token)) return true;
        return false;
    }

    private static bool FuzzyField(string text, string token)
    {
        if (Subsequence(text, token)) return true;
        int max = token.Length < 6 ? 1 : 2;                 // ≤1 for short tokens, ≤2 for longer
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            if (Math.Abs(word.Length - token.Length) <= max && Levenshtein(word, token, max) <= max)
                return true;
        return false;
    }

    /// <summary>True when every char of <paramref name="token"/> appears in order within <paramref name="text"/>.</summary>
    private static bool Subsequence(string text, string token)
    {
        int j = 0;
        for (int i = 0; i < text.Length && j < token.Length; i++)
            if (text[i] == token[j]) j++;
        return j == token.Length;
    }

    /// <summary>Levenshtein distance, capped: returns <paramref name="max"/>+1 once it is exceeded.</summary>
    private static int Levenshtein(string a, string b, int max)
    {
        int n = a.Length, m = b.Length;
        var prev = new int[m + 1];
        var cur = new int[m + 1];
        for (int j = 0; j <= m; j++) prev[j] = j;
        for (int i = 1; i <= n; i++)
        {
            cur[0] = i;
            int rowMin = cur[0];
            for (int j = 1; j <= m; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
                if (cur[j] < rowMin) rowMin = cur[j];
            }
            if (rowMin > max) return max + 1;   // whole row past the cap → no point continuing
            (prev, cur) = (cur, prev);
        }
        return prev[m];
    }
}
