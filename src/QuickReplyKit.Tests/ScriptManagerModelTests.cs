using System.IO;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Scripts;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Services;
using QuickReplyKit.ViewModels;

namespace QuickReplyKit.Tests;

public class ScriptManagerModelTests
{
    [Fact]
    public void SaveAndTrust_TrustsOnlyAfterVerifiedSave_AndSourceChangeRevokes()
    {
        using var temp = new TempDir();
        var trust = OpenTrust(temp.Of("trust.json"));
        var model = new ScriptManagerModel(temp.Of("library"), trust, () => { });
        model.Reload();

        var created = model.Save(null, "Greeting", "\"hello\"", true, trustAfterSave: true);

        Assert.True(created.Success, created.Error);
        Assert.True(created.Item!.IsTrusted);
        Assert.True(ScriptDefinitionFile.Load(created.Item.FullPath).Success);

        var changed = model.Save(created.Item, "Greeting", "\"changed\"", true, trustAfterSave: false);

        Assert.True(changed.Success, changed.Error);
        Assert.False(changed.Item!.IsTrusted);
        Assert.False(trust.IsTrusted(changed.Item.Definition));
    }

    [Fact]
    public void Save_RejectsInvalidAndDuplicateNames_WithoutReplacingFiles()
    {
        using var temp = new TempDir();
        var model = new ScriptManagerModel(temp.Of("library"), OpenTrust(temp.Of("trust.json")), () => { });
        model.Reload();
        Assert.False(model.Save(null, "Bad", "upper()", true, false).Success);
        Assert.Empty(Directory.Exists(temp.Of("library"))
            ? Directory.EnumerateFiles(temp.Of("library")) : []);

        var first = model.Save(null, "Greeting", "\"one\"", true, false);
        Assert.True(first.Success);
        var duplicate = model.Save(null, "greeting", "\"two\"", true, false);
        Assert.False(duplicate.Success);
        Assert.Single(Directory.EnumerateFiles(temp.Of("library")));
    }

    [Fact]
    public void TrustAndDelete_OperateOnOneSelectedDefinition()
    {
        using var temp = new TempDir();
        var trust = OpenTrust(temp.Of("trust.json"));
        int changes = 0;
        var model = new ScriptManagerModel(temp.Of("library"), trust, () => changes++);
        model.Reload();
        var saved = model.Save(null, "Clock", "time()", false, false).Item!;

        var trusted = model.Trust(saved);
        Assert.True(trusted.Success, trusted.Error);
        Assert.True(trusted.Item!.IsTrusted);

        var deleted = model.Delete(trusted.Item);
        Assert.True(deleted.Success, deleted.Error);
        Assert.False(File.Exists(saved.FullPath));
        Assert.False(trust.IsTrusted(saved.Definition));
        Assert.Equal(3, changes);
    }

    [Fact]
    public void Catalog_ListsInvalidAndDuplicateDefinitions_WithTextualStatus()
    {
        using var temp = new TempDir();
        string root = temp.Of("library");
        var trust = OpenTrust(temp.Of("trust.json"));
        Save(root, "one", new("1", "Same", ScriptDefinitionFile.Language, true, "\"one\""));
        Save(root, "two", new("2", "same", ScriptDefinitionFile.Language, true, "\"two\""));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "broken" + ScriptDefinitionFile.Extension), "{");

        var items = ScriptCatalog.Load(root, trust).Items;

        Assert.Equal(3, items.Count);
        Assert.Equal(2, items.Count(x => x.Status == "Duplicate name"));
        Assert.Single(items, x => x.Status == "Invalid");
        Assert.All(items, x => Assert.False(x.IsTrusted));
    }

    [Fact]
    public void LauncherScriptNames_OnlyReturnsEnabledValidTrustedDefinitions()
    {
        using var temp = new TempDir();
        string root = temp.Of("library");
        var trust = OpenTrust(temp.Of("trust.json"));
        var yes = new ScriptDefinition("yes", "Usable", ScriptDefinitionFile.Language, true, "\"yes\"");
        var off = new ScriptDefinition("off", "Disabled", ScriptDefinitionFile.Language, false, "\"off\"");
        Save(root, "yes", yes);
        Save(root, "off", off);
        Assert.True(trust.Trust(yes).Success);
        Assert.True(trust.Trust(off).Success);
        using var library = new LibraryService(root, recentPath: temp.Of("recent.json"), watch: false, scriptTrust: trust);
        var vm = new LauncherViewModel(library, new NoopInsert(), new AppConfig { LibraryPath = root });

        Assert.Equal(["Usable"], vm.ScriptNames());
    }

    private static ScriptTrustLedger OpenTrust(string path)
    {
        var opened = ScriptTrustLedger.Open(path);
        Assert.NotNull(opened.Value);
        return opened.Value;
    }

    private static void Save(string root, string file, ScriptDefinition definition)
    {
        string path = Path.Combine(root, file + ScriptDefinitionFile.Extension);
        var result = ScriptDefinitionFile.Save(path, definition);
        Assert.True(result.Success, result.Error);
    }

    private sealed class NoopInsert : IInsertService
    {
        public void InsertRich(ResponseItem item) { }
        public void InsertPlain(ResponseItem item) { }
        public void Copy(ResponseItem item) { }
    }
}
