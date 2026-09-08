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
    public void Save_NativeLanguageRoundTrips_AndExistingLanguageIsPreservedByCompatibleApi()
    {
        using var temp = new TempDir();
        var model = new ScriptManagerModel(temp.Of("library"), OpenTrust(temp.Of("trust.json")), () => { });
        model.Reload();

        var created = model.Save(null, "Native", "Write-Output 'hello'", true,
            ScriptDefinitionFile.NativeLanguage, trustAfterSave: false);
        Assert.True(created.Success, created.Error);
        var savedAgain = model.Save(created.Item, "Native", "Write-Output 'updated'", true,
            trustAfterSave: false);

        Assert.True(savedAgain.Success, savedAgain.Error);
        Assert.Equal(ScriptDefinitionFile.NativeLanguage, savedAgain.Item!.Definition!.Language);
        Assert.Equal(ScriptDefinitionFile.NativeLanguage,
            ScriptDefinitionFile.Load(savedAgain.Item.FullPath).Value!.Language);
    }

    [Fact]
    public void Save_ChangingLanguageRevokesTrustEvenWhenSourceIsUnchanged()
    {
        using var temp = new TempDir();
        var trust = OpenTrust(temp.Of("trust.json"));
        var model = new ScriptManagerModel(temp.Of("library"), trust, () => { });
        model.Reload();
        var created = model.Save(null, "Literal", "\"hello\"", true, trustAfterSave: true).Item!;

        var changed = model.Save(created, "Literal", "\"hello\"", true,
            ScriptDefinitionFile.NativeLanguage, trustAfterSave: false);

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

        var trusted = model.Trust(saved, saved.Definition!);
        Assert.True(trusted.Success, trusted.Error);
        Assert.True(trusted.Item!.IsTrusted);

        var deleted = model.Delete(trusted.Item);
        Assert.True(deleted.Success, deleted.Error);
        Assert.False(File.Exists(saved.FullPath));
        Assert.False(trust.IsTrusted(saved.Definition));
        Assert.Equal(3, changes);
    }

    [Fact]
    public void Trust_RefusesSourceChangedAfterReview_ThenRequiresFreshApproval()
    {
        using var temp = new TempDir();
        var trust = OpenTrust(temp.Of("trust.json"));
        var model = new ScriptManagerModel(temp.Of("library"), trust, () => { });
        model.Reload();
        var reviewed = model.Save(null, "Greeting", "\"reviewed\"", true, false).Item!;
        var changed = reviewed.Definition! with { Expression = "\"changed on disk\"" };
        Assert.True(ScriptDefinitionFile.Save(reviewed.FullPath, changed).Success);

        var refused = model.Trust(reviewed, reviewed.Definition!);

        Assert.False(refused.Success);
        Assert.Contains("changed on disk", refused.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(changed, refused.Item!.Definition);
        Assert.False(trust.IsTrusted(changed));

        var approved = model.Trust(refused.Item, refused.Item.Definition!);
        Assert.True(approved.Success, approved.Error);
        Assert.True(trust.IsTrusted(changed));
    }

    [Fact]
    public void DraftGuard_CleanDraftDoesNotPrompt()
    {
        int prompts = 0, saves = 0;

        bool proceed = Views.ScriptManagerWindow.CanLeaveDraft(false,
            () => { prompts++; return Views.UnsavedDraftChoice.Cancel; },
            () => { saves++; return false; });

        Assert.True(proceed);
        Assert.Equal(0, prompts);
        Assert.Equal(0, saves);
    }

    [Theory]
    [InlineData(1, true, true)]
    [InlineData(2, false, false)]
    [InlineData(0, true, true)]
    [InlineData(0, false, false)]
    public void DraftGuard_RespectsChoiceAndFailedSave(int choiceValue, bool saveSucceeds, bool expected)
    {
        var choice = (Views.UnsavedDraftChoice)choiceValue;
        int saves = 0;

        bool proceed = Views.ScriptManagerWindow.CanLeaveDraft(true, () => choice,
            () => { saves++; return saveSucceeds; });

        Assert.Equal(expected, proceed);
        Assert.Equal(choice == Views.UnsavedDraftChoice.Save ? 1 : 0, saves);
    }

    [Fact]
    public void DraftGuard_IsWiredToSelectionNewCloseAndEscape()
    {
        string code = File.ReadAllText(TestFiles.RepoAsset(
            "src/QuickReplyKit/Views/ScriptManagerWindow.xaml.cs"));
        string xaml = File.ReadAllText(TestFiles.RepoAsset(
            "src/QuickReplyKit/Views/ScriptManagerWindow.xaml"));

        Assert.Contains("ScriptList_SelectionChanged", code, StringComparison.Ordinal);
        Assert.Contains("if (!TryLeaveDraft())", code, StringComparison.Ordinal);
        Assert.Contains("if (TryLeaveDraft()) NewDraft();", code, StringComparison.Ordinal);
        Assert.Contains("private void Window_Closing", code, StringComparison.Ordinal);
        Assert.Contains("private void Window_PreviewKeyDown", code, StringComparison.Ordinal);
        Assert.Contains("Closing=\"Window_Closing\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviewKeyDown=\"Window_PreviewKeyDown\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeTrustConfirmation_CancelStopsTrust_RestrictedDoesNotPrompt()
    {
        int prompts = 0;

        Assert.False(Views.ScriptManagerWindow.ConfirmTrust(ScriptDefinitionFile.NativeLanguage,
            () => { prompts++; return false; }));
        Assert.True(Views.ScriptManagerWindow.ConfirmTrust(ScriptDefinitionFile.Language,
            () => { prompts++; return false; }));
        Assert.Equal(1, prompts);
    }

    [Fact]
    public void ManagerUi_OffersBothLanguagesAndKeepsFullTrustWarningVisible()
    {
        string xaml = File.ReadAllText(TestFiles.RepoAsset(
            "src/QuickReplyKit/Views/ScriptManagerWindow.xaml"));
        string code = File.ReadAllText(TestFiles.RepoAsset(
            "src/QuickReplyKit/Views/ScriptManagerWindow.xaml.cs"));

        Assert.Contains("Tag=\"qrk-expr-1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"powershell-1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NativeWarning\"", xaml, StringComparison.Ordinal);
        Assert.Contains("signed-in Windows user", xaml, StringComparison.Ordinal);
        Assert.Contains("ConfirmTrust(SelectedLanguage", code, StringComparison.Ordinal);
        Assert.Contains("ConfirmTrust(reviewed.Language", code, StringComparison.Ordinal);
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
