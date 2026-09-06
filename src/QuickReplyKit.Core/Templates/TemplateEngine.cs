using System.Globalization;
using System.Text;

namespace QuickReplyKit.Core.Templates;

/// <summary>
/// Resolves <c>{{ token }}</c> occurrences inside the stored HTML subset at insert/preview time.
///
/// Two kinds of token:
///   • Built-in variables — a FIXED, allow-listed set of pure, deterministic functions of the
///     supplied clock (greeting/date/time/day/date:&lt;format&gt;). This is the whole vocabulary:
///     an imported/shared response can NEVER cause arbitrary code to run — there is no eval hook.
///   • Includes — any other name is looked up (case-insensitive) via <paramref name="lookupHtmlByName"/>
///     and its stored HTML is spliced in and resolved recursively, enabling {{welcome}}/{{closing}}
///     composition. Cycles and excessive depth emit an inline <c>[cycle: name]</c> marker and stop
///     (never hang/throw); an unknown name emits <c>[missing: name]</c>.
///
/// Literal braces: write <c>{{{{</c> to output a literal <c>{{</c> (so <c>{{{{name}}</c> → <c>{{name}}</c>).
/// Names are case-insensitive and whitespace inside the braces is trimmed. Output stays within the
/// HTML subset (markers are plain bracketed text, no markup).
/// </summary>
public static class TemplateEngine
{
    /// <summary>Safety net for pathological include chains that escape cycle detection.</summary>
    private const int MaxDepth = 32;

    /// <summary>
    /// Cap on total include expansions per resolve. Cycle detection stops loops, but a non-cyclic
    /// fan-out DAG (each response including the next twice: t0→t1 t1→t2 t2…) expands exponentially
    /// within <see cref="MaxDepth"/>. This budget bounds the total work so a crafted/shared library
    /// can never hang the app. Far above any realistic template (which uses a handful of includes).
    /// </summary>
    private const int MaxIncludes = 1000;

    /// <summary>
    /// Allow-listed built-ins. Each is a pure function of (now, optional arg). This dictionary is the
    /// complete set of executable behaviour — extend it by adding another pure function, nothing else.
    /// </summary>
    private static readonly Dictionary<string, Func<DateTimeOffset, string?, string>> Builtins =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["greeting"] = static (now, _) => Greeting(now),
            ["date"]     = static (now, arg) => string.IsNullOrEmpty(arg)
                                ? now.ToString("d", CultureInfo.CurrentCulture)
                                : FormatDate(now, arg),
            ["time"]     = static (now, _) => now.ToString("t", CultureInfo.CurrentCulture),
            ["day"]      = static (now, _) => now.ToString("dddd", CultureInfo.CurrentCulture),
        };

    /// <summary>Resolve every token in <paramref name="html"/>. Never throws; never hangs.</summary>
    public static string Resolve(string html, Func<string, string?> lookupHtmlByName, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(html)) return html ?? "";
        var lookup = lookupHtmlByName ?? (static _ => null);
        return ResolveInternal(html, lookup, now, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0,
            new[] { MaxIncludes });
    }

    /// <summary>True if the text contains at least one <c>{{…}}</c> token (ignoring <c>{{{{</c> escapes).</summary>
    public static bool ContainsTokens(string? html)
    {
        if (string.IsNullOrEmpty(html)) return false;
        int i = 0;
        while (i < html.Length)
        {
            if (IsEscape(html, i)) { i += 4; continue; }
            if (IsOpen(html, i))
            {
                if (html.IndexOf("}}", i + 2, StringComparison.Ordinal) >= 0) return true;
                i += 2;
                continue;
            }
            i++;
        }
        return false;
    }

    private static string ResolveInternal(
        string html, Func<string, string?> lookup, DateTimeOffset now, HashSet<string> path, int depth, int[] budget)
    {
        var sb = new StringBuilder(html.Length);
        int i = 0;
        while (i < html.Length)
        {
            if (IsEscape(html, i)) { sb.Append("{{"); i += 4; continue; }
            if (IsOpen(html, i))
            {
                int close = html.IndexOf("}}", i + 2, StringComparison.Ordinal);
                if (close < 0) { sb.Append(html, i, html.Length - i); break; } // no closer: literal tail
                string token = html.Substring(i + 2, close - (i + 2)).Trim();
                sb.Append(ResolveToken(token, lookup, now, path, depth, budget));
                i = close + 2;
                continue;
            }
            sb.Append(html[i]);
            i++;
        }
        return sb.ToString();
    }

    private static string ResolveToken(
        string token, Func<string, string?> lookup, DateTimeOffset now, HashSet<string> path, int depth, int[] budget)
    {
        if (token.Length == 0) return "";

        string name = token;
        string? arg = null;
        int colon = token.IndexOf(':');
        if (colon >= 0)
        {
            name = token[..colon].Trim();
            arg = token[(colon + 1)..].Trim();
        }

        if (Builtins.TryGetValue(name, out var fn)) return fn(now, arg);

        return ResolveInclude(token, lookup, now, path, depth, budget); // whole token is the response name
    }

    private static string ResolveInclude(
        string name, Func<string, string?> lookup, DateTimeOffset now, HashSet<string> path, int depth, int[] budget)
    {
        if (depth >= MaxDepth || path.Contains(name)) return $"[cycle: {name}]";
        if (--budget[0] < 0) return $"[includes: too many]"; // non-cyclic fan-out bomb: stop, never hang
        string? body = lookup(name);
        if (body is null) return $"[missing: {name}]";

        path.Add(name);
        try { return ResolveInternal(body, lookup, now, path, depth + 1, budget); }
        finally { path.Remove(name); }
    }

    private static string Greeting(DateTimeOffset now) =>
        now.Hour < 12 ? "Good morning" : now.Hour < 17 ? "Good afternoon" : "Good evening";

    private static string FormatDate(DateTimeOffset now, string format)
    {
        try { return now.ToString(format, CultureInfo.CurrentCulture); }
        catch (FormatException) { return $"[date: invalid format]"; }
    }

    private static bool IsOpen(string s, int i) =>
        i + 1 < s.Length && s[i] == '{' && s[i + 1] == '{';

    private static bool IsEscape(string s, int i) =>
        i + 3 < s.Length && s[i] == '{' && s[i + 1] == '{' && s[i + 2] == '{' && s[i + 3] == '{';
}
