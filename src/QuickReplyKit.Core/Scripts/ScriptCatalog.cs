using System.IO;
using System.Net;

namespace QuickReplyKit.Core.Scripts;

/// <summary>Immutable, fail-closed view of script definitions in one response library.</summary>
public sealed class ScriptCatalog
{
    private sealed record Entry(ScriptDefinition? Definition, string? Error, bool Duplicate);
    private sealed record Candidate(string Path, string RelativePath, string Name,
        ScriptDefinition? Definition, string? Error);

    private readonly IReadOnlyDictionary<string, Entry> _entries;
    private readonly ScriptTrustLedger _trust;

    private ScriptCatalog(IReadOnlyDictionary<string, Entry> entries,
        IReadOnlyList<ScriptCatalogItem> items, ScriptTrustLedger trust)
    {
        _entries = entries;
        Items = items;
        _trust = trust;
    }

    /// <summary>Every portable definition, including invalid and duplicate entries, in path order.</summary>
    public IReadOnlyList<ScriptCatalogItem> Items { get; }

    public static ScriptCatalog Empty(ScriptTrustLedger trust) =>
        new(new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase), [], trust);

    public static ScriptCatalog Load(string libraryRoot, ScriptTrustLedger trust)
    {
        if (!Directory.Exists(libraryRoot)) return Empty(trust);

        var candidates = new List<Candidate>();
        try
        {
            foreach (string path in Directory.EnumerateFiles(libraryRoot, "*" + ScriptDefinitionFile.Extension,
                         SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var loaded = ScriptDefinitionFile.Load(path);
                string fallbackName = Path.GetFileName(path)[..^ScriptDefinitionFile.Extension.Length];
                candidates.Add(new(path, Path.GetRelativePath(libraryRoot, path).Replace('\\', '/'),
                    loaded.Value?.Name ?? fallbackName, loaded.Value, loaded.Error));
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
        var duplicateNames = candidates.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = candidates.Select(candidate => new ScriptCatalogItem(
            candidate.Path, candidate.RelativePath, candidate.Name, candidate.Definition,
            candidate.Definition is not null && !duplicateNames.Contains(candidate.Name),
            duplicateNames.Contains(candidate.Name),
            candidate.Definition is not null && !duplicateNames.Contains(candidate.Name) &&
                trust.IsTrusted(candidate.Definition),
            candidate.Error)).ToArray();
        return new(entries, items, trust);
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
        var result = ScriptExpression.Evaluate(entry.Definition.Expression, now);
        return result.Success ? result : result with
        {
            Error = $"Script '{entry.Definition.Name}': {result.Error}",
        };
    }

    private static ScriptEvaluationResult Fail(string state, string name) =>
        new(false, $"[script {state}: {WebUtility.HtmlEncode(name)}]", $"Script '{name}' is {state}.");
}

/// <summary>Read-only status used by management and diagnostics; expression source is never logged.</summary>
public sealed record ScriptCatalogItem(
    string FullPath,
    string RelativePath,
    string Name,
    ScriptDefinition? Definition,
    bool IsValid,
    bool IsDuplicate,
    bool IsTrusted,
    string? Error)
{
    public bool IsEnabled => Definition?.Enabled == true;
    public string Status => !IsValid ? IsDuplicate ? "Duplicate name" : "Invalid"
        : !IsEnabled ? IsTrusted ? "Disabled · trusted" : "Disabled · untrusted"
        : IsTrusted ? "Enabled · trusted" : "Enabled · untrusted";
}
