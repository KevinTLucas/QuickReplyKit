using System.IO;
using QuickReplyKit.Core.Scripts;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.ViewModels;

internal sealed record ScriptManagerResult(bool Success, string? Error, ScriptCatalogItem? Item = null);

/// <summary>Small, testable coordinator for one-script-at-a-time management.</summary>
internal sealed class ScriptManagerModel
{
    private readonly string _root;
    private readonly ScriptTrustLedger _trust;
    private readonly Action _changed;

    internal ScriptManagerModel(string root, ScriptTrustLedger trust, Action changed)
    {
        _root = root;
        _trust = trust;
        _changed = changed;
    }

    public IReadOnlyList<ScriptCatalogItem> Items { get; private set; } = [];

    public void Reload() => Items = ScriptCatalog.Load(_root, _trust).Items;

    public ScriptOperationResult Validate(string name, string expression, bool enabled,
        ScriptCatalogItem? existing = null) => ScriptDefinitionFile.Validate(Draft(name, expression, enabled, existing));

    public ScriptManagerResult Save(ScriptCatalogItem? existing, string name, string expression,
        bool enabled, bool trustAfterSave)
    {
        name = name.Trim();
        var definition = Draft(name, expression, enabled, existing);
        var valid = ScriptDefinitionFile.Validate(definition);
        if (!valid.Success) return new(false, valid.Error);
        if (Items.Any(x => x.FullPath != existing?.FullPath && x.Definition is not null &&
            string.Equals(x.Definition.Name, name, StringComparison.OrdinalIgnoreCase)))
            return new(false, "Another script already uses that name.");

        string path = existing?.FullPath ?? NewPath(name);
        if (existing is null && File.Exists(path)) return new(false, "A script file with that name already exists.");

        bool sourceChanged = existing?.Definition is { } old &&
            !string.Equals(old.Expression, expression, StringComparison.Ordinal);
        if (sourceChanged)
        {
            var revoked = _trust.Revoke(definition.Id);
            if (!revoked.Success) return new(false, revoked.Error);
        }

        var saved = ScriptDefinitionFile.Save(path, definition);
        if (!saved.Success) return new(false, saved.Error);
        var reloaded = ScriptDefinitionFile.Load(path);
        if (!reloaded.Success || reloaded.Value != definition)
            return new(false, reloaded.Error ?? "The saved script could not be verified.");

        if (trustAfterSave)
        {
            var trusted = _trust.Trust(reloaded.Value);
            if (!trusted.Success) return new(false, trusted.Error);
        }

        _changed();
        Reload();
        return new(true, null, Items.FirstOrDefault(x =>
            string.Equals(x.FullPath, path, StringComparison.OrdinalIgnoreCase)));
    }

    public ScriptManagerResult Trust(ScriptCatalogItem item)
    {
        var loaded = ScriptDefinitionFile.Load(item.FullPath);
        if (!loaded.Success) return new(false, loaded.Error);
        var result = _trust.Trust(loaded.Value!);
        if (!result.Success) return new(false, result.Error);
        _changed();
        Reload();
        return new(true, null, Find(item.FullPath));
    }

    public ScriptManagerResult Delete(ScriptCatalogItem item)
    {
        try
        {
            if (item.Definition is { } definition)
            {
                var revoked = _trust.Revoke(definition.Id);
                if (!revoked.Success) return new(false, revoked.Error);
            }
            File.Delete(item.FullPath);
            _changed();
            Reload();
            return new(true, null);
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    private ScriptCatalogItem? Find(string path) => Items.FirstOrDefault(x =>
        string.Equals(x.FullPath, path, StringComparison.OrdinalIgnoreCase));

    private string NewPath(string name)
    {
        string file = ResponseManager.Sanitize(name) ?? "Script";
        return Path.Combine(_root, file + ScriptDefinitionFile.Extension);
    }

    private static ScriptDefinition Draft(string name, string expression, bool enabled,
        ScriptCatalogItem? existing) => new(existing?.Definition?.Id ?? Guid.NewGuid().ToString("N"),
            name, ScriptDefinitionFile.Language, enabled, expression);
}
