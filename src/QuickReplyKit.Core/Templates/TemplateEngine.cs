using System.Globalization;
using System.Text;
using QuickReplyKit.Core.Diagnostics;
using QuickReplyKit.Core.Scripts;

namespace QuickReplyKit.Core.Templates;

public sealed record TemplateResolutionResult(string Html, IReadOnlyList<string> Errors)
{
    public bool Success => Errors.Count == 0;
    public string ErrorText => string.Join(Environment.NewLine, Errors.Take(3).Select(error => "- " + error)) +
        (Errors.Count > 3 ? $"{Environment.NewLine}- {Errors.Count - 3} more script error(s)." : "");
}

/// <summary>
/// Resolves allow-listed built-ins, response includes, and optional sandboxed scripts in stored HTML.
/// Script tokens execute only in text-node content; cycles and all limits produce visible markers.
/// </summary>
public static class TemplateEngine
{
    private const int MaxDepth = 32;
    private const int MaxIncludes = 1000;
    public const int MaxScriptCalls = 64;
    public const int MaxResolvedChars = 250_000;
    private const string OutputLimitMarker = "[template error: output-limit]";

    private sealed class Context(
        Func<string, string?> lookup,
        Func<string, DateTimeOffset, ScriptEvaluationResult>? evaluateScript,
        DateTimeOffset now,
        Metrics metrics)
    {
        public readonly Func<string, string?> Lookup = lookup;
        public readonly Func<string, DateTimeOffset, ScriptEvaluationResult>? EvaluateScript = evaluateScript;
        public readonly DateTimeOffset Now = now;
        public readonly Metrics Metrics = metrics;
        public readonly HashSet<string> Path = new(StringComparer.OrdinalIgnoreCase);
        public readonly StringBuilder Output = new();
        public readonly List<string> Errors = [];
        public int IncludesLeft = MaxIncludes;
        public int ScriptCalls;
        public bool OutputLimited;

        public void Append(char value)
        {
            if (OutputLimited) return;
            if (Output.Length == MaxResolvedChars) { OutputLimited = true; return; }
            Output.Append(value);
        }

        public void Append(string value)
        {
            if (OutputLimited || value.Length == 0) return;
            if (value.Length > MaxResolvedChars - Output.Length) { OutputLimited = true; return; }
            Output.Append(value);
        }
    }

    private sealed class Metrics
    {
        public int Tokens;
        public int Builtins;
        public int IncludeAttempts;
        public int ResolvedIncludes;
        public int Missing;
        public int Cycles;
        public int Limited;
        public int Scripts;
        public int ScriptFailures;
    }

    private static readonly Dictionary<string, Func<DateTimeOffset, string?, string>> Builtins =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["greeting"] = static (now, _) => Greeting(now),
            ["date"] = static (now, arg) => string.IsNullOrEmpty(arg)
                ? now.ToString("d", CultureInfo.CurrentCulture)
                : FormatDate(now, arg),
            ["time"] = static (now, _) => now.ToString("t", CultureInfo.CurrentCulture),
            ["day"] = static (now, _) => now.ToString("dddd", CultureInfo.CurrentCulture),
        };

    /// <summary>
    /// Resolve every token. <paramref name="evaluateScript"/> receives the same caller-captured clock
    /// for the complete render. Existing callers may omit it; script tokens then fail closed.
    /// </summary>
    public static string Resolve(string html, Func<string, string?> lookupHtmlByName, DateTimeOffset now,
        Func<string, DateTimeOffset, ScriptEvaluationResult>? evaluateScript = null)
        => ResolveDetailed(html, lookupHtmlByName, now, evaluateScript).Html;

    public static TemplateResolutionResult ResolveDetailed(string html,
        Func<string, string?> lookupHtmlByName, DateTimeOffset now,
        Func<string, DateTimeOffset, ScriptEvaluationResult>? evaluateScript = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (string.IsNullOrEmpty(html))
        {
            Log(0, 0, new Metrics(), sw.ElapsedMilliseconds);
            return new(html ?? "", []);
        }

        var metrics = new Metrics();
        var context = new Context(lookupHtmlByName ?? (static _ => null), evaluateScript, now, metrics);
        ResolveInternal(html, context, 0, scriptsAllowed: true);
        string output = context.OutputLimited ? OutputLimitMarker : context.Output.ToString();
        Log(html.Length, output.Length, metrics, sw.ElapsedMilliseconds);
        return new(output, context.Errors.Distinct(StringComparer.Ordinal).ToArray());
    }

    /// <summary>True when text contains a real <c>{{…}}</c> token, ignoring <c>{{{{</c> escapes.</summary>
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

    private static void ResolveInternal(string html, Context context, int depth, bool scriptsAllowed)
    {
        bool inTag = false;
        char quote = '\0';

        for (int i = 0; i < html.Length && !context.OutputLimited;)
        {
            if (IsEscape(html, i)) { context.Append("{{"); i += 4; continue; }
            if (IsOpen(html, i))
            {
                int close = html.IndexOf("}}", i + 2, StringComparison.Ordinal);
                if (close < 0)
                {
                    context.Append(html[i..]);
                    break;
                }

                string token = html.Substring(i + 2, close - i - 2).Trim();
                context.Metrics.Tokens++;
                ResolveToken(token, html.Substring(i, close + 2 - i), context, depth,
                    scriptsAllowed && !inTag);
                i = close + 2;
                continue;
            }

            char value = html[i++];
            context.Append(value);
            if (!inTag)
            {
                if (value == '<') inTag = true;
            }
            else if (quote == '\0')
            {
                if (value is '\'' or '"') quote = value;
                else if (value == '>') inTag = false;
            }
            else if (value == quote)
            {
                quote = '\0';
            }
        }
    }

    private static void ResolveToken(string token, string literal, Context context, int depth, bool scriptAllowed)
    {
        if (token.Length == 0) return;

        string name = token;
        string? arg = null;
        int colon = token.IndexOf(':');
        if (colon >= 0)
        {
            name = token[..colon].Trim();
            arg = token[(colon + 1)..].Trim();
        }

        if (name.Equals("script", StringComparison.OrdinalIgnoreCase))
        {
            if (!scriptAllowed) { context.Append(literal); return; }
            ResolveScript(arg ?? "", context);
            return;
        }

        if (Builtins.TryGetValue(name, out var builtin))
        {
            context.Metrics.Builtins++;
            context.Append(builtin(context.Now, arg));
            return;
        }

        ResolveInclude(token, context, depth, scriptAllowed);
    }

    private static void ResolveScript(string name, Context context)
    {
        if (++context.ScriptCalls > MaxScriptCalls)
        {
            context.Metrics.Limited++;
            context.Metrics.ScriptFailures++;
            context.Append("[scripts: too many]");
            context.Errors.Add("This response calls too many scripts.");
            return;
        }

        context.Metrics.Scripts++;
        if (context.EvaluateScript is null)
        {
            context.Metrics.ScriptFailures++;
            context.Append($"[script missing: {System.Net.WebUtility.HtmlEncode(name)}]");
            context.Errors.Add("Script evaluation is unavailable.");
            return;
        }

        try
        {
            var result = context.EvaluateScript(name, context.Now);
            context.Append(result.Output);
            if (!result.Success)
            {
                context.Metrics.ScriptFailures++;
                context.Errors.Add(result.Error ?? "A response script failed.");
            }
        }
        catch
        {
            context.Metrics.ScriptFailures++;
            context.Append("[script error: evaluation-failed]");
            context.Errors.Add("A response script failed unexpectedly.");
        }
    }

    private static void ResolveInclude(string name, Context context, int depth, bool scriptsAllowed)
    {
        context.Metrics.IncludeAttempts++;
        if (depth >= MaxDepth || context.Path.Contains(name))
        {
            context.Metrics.Cycles++;
            context.Append($"[cycle: {name}]");
            return;
        }
        if (--context.IncludesLeft < 0)
        {
            context.Metrics.Limited++;
            context.Append("[includes: too many]");
            return;
        }

        string? body;
        try { body = context.Lookup(name); }
        catch { body = null; }
        if (body is null)
        {
            context.Metrics.Missing++;
            context.Append($"[missing: {name}]");
            return;
        }

        context.Metrics.ResolvedIncludes++;
        context.Path.Add(name);
        try { ResolveInternal(body, context, depth + 1, scriptsAllowed); }
        finally { context.Path.Remove(name); }
    }

    private static void Log(int inputLength, int outputLength, Metrics metrics, long elapsedMs) =>
        Diag.Verbose("TemplateEngine", "Template",
            $"resolve complete inputLength={inputLength} outputLength={outputLength} tokens={metrics.Tokens} builtins={metrics.Builtins} includeAttempts={metrics.IncludeAttempts} resolvedIncludes={metrics.ResolvedIncludes} scripts={metrics.Scripts} scriptFailures={metrics.ScriptFailures} missing={metrics.Missing} cycles={metrics.Cycles} limited={metrics.Limited} elapsedMs={elapsedMs}");

    private static string Greeting(DateTimeOffset now) =>
        now.Hour < 12 ? "Good morning" : now.Hour < 17 ? "Good afternoon" : "Good evening";

    private static string FormatDate(DateTimeOffset now, string format)
    {
        try { return now.ToString(format, CultureInfo.CurrentCulture); }
        catch (FormatException) { return "[date: invalid format]"; }
    }

    private static bool IsOpen(string value, int index) =>
        index + 1 < value.Length && value[index] == '{' && value[index + 1] == '{';

    private static bool IsEscape(string value, int index) =>
        index + 3 < value.Length && value[index] == '{' && value[index + 1] == '{' &&
        value[index + 2] == '{' && value[index + 3] == '{';
}
