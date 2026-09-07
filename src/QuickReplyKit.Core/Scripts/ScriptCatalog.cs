using System.IO;
using System.Net;

namespace QuickReplyKit.Core.Scripts;

/// <summary>Immutable, fail-closed view of script definitions in one response library.</summary>
public sealed class ScriptCatalog
{
    private sealed record Entry(ScriptDefinition? Definition, string? Error, bool Duplicate);

    private readonly IReadOnlyDictionary<string, Entry> _entries;
    private readonly ScriptTrustLedger _trust;

    private ScriptCatalog(IReadOnlyDictionary<string, Entry> entries, ScriptTrustLedger trust)
    {
        _entries = entries;
        _trust = trust;
    }

    public static ScriptCatalog Empty(ScriptTrustLedger trust) =>
        new(new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase), trust);

    public static ScriptCatalog Load(string libraryRoot, ScriptTrustLedger trust)
    {
        if (!Directory.Exists(libraryRoot)) return Empty(trust);

        var candidates = new List<(string Name, ScriptDefinition? Definition, string? Error)>();
        try
        {
            foreach (string path in Directory.EnumerateFiles(libraryRoot, "*" + ScriptDefinitionFile.Extension,
                         SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var loaded = ScriptDefinitionFile.Load(path);
                string fallbackName = Path.GetFileName(path)[..^ScriptDefinitionFile.Extension.Length];
                candidates.Add((loaded.Value?.Name ?? fallbackName, loaded.Value, loaded.Error));
            }
        }
        catch
        {
            return Empty(trust);
        }

        var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in candidates.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var values = group.ToArray();
            var first = values[0];
            entries[group.Key] = new(first.Definition, first.Error, values.Length > 1);
        }
        return new(entries, trust);
    }

    /// <summary>Evaluate one valid, enabled, uniquely named and locally trusted definition.</summary>
    public ScriptEvaluationResult Evaluate(string name, DateTimeOffset now)
    {
        name = name.Trim();
        if (!_entries.TryGetValue(name, out var entry)) return Fail("missing", name);
        if (entry.Duplicate) return Fail("duplicate", name);
        if (entry.Definition is null) return Fail("invalid", name);
        if (!entry.Definition.Enabled) return Fail("disabled", name);
        if (!_trust.IsTrusted(entry.Definition)) return Fail("untrusted", name);
        return ScriptExpression.Evaluate(entry.Definition.Expression, now);
    }

    private static ScriptEvaluationResult Fail(string state, string name) =>
        new(false, $"[script {state}: {WebUtility.HtmlEncode(name)}]", $"Script '{name}' is {state}.");
}
