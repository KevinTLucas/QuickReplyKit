using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using QuickReplyKit.Core.ImportExport;
using QuickReplyKit.Core.Scripts;
using QuickReplyKit.ViewModels;

namespace QuickReplyKit.Tests;

public class ScriptImportExportTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const string Response =
        "{\"schema\":1,\"app\":\"\",\"tags\":[],\"html\":\"<p>x</p>\",\"favorite\":false}";

    [Fact]
    public void MixedArchive_ClassifiesScriptsAndResponses_AndExposesExactExpression()
    {
        using var t = new TempDir();
        string source = t.Of("mixed.zip");
        MakeZip(source,
            ("Answer.json", Response),
            ("Greeting.qrkscript.json", Script("greeting", "Greeting", "concat(\"Hi \", upper(\"team\"))")),
            ("Broken.qrkscript.json", "{}"));

        var plan = LibraryPorter.PlanImport(t.Of("lib"), source, "Shared");

        Assert.Equal([ImportItemKind.Response, ImportItemKind.Script, ImportItemKind.Script],
            plan.Entries.Select(entry => entry.Kind));
        Assert.Equal("concat(\"Hi \", upper(\"team\"))", plan.Entries[1].CodeSource);
        Assert.Equal(ImportClassification.Invalid, plan.Entries[2].Classification);
    }

    [Fact]
    public void Review_ScriptsStartUnchecked_AreNeverBulkSelected_AndRequireIndividualAcceptance()
    {
        using var t = new TempDir();
        string source = t.Of("mixed.zip");
        MakeZip(source,
            ("Answer.json", Response),
            ("Greeting.qrkscript.json", Script("greeting", "Greeting", "\"hello\"")));
        var model = new ImportReviewModel(LibraryPorter.PlanImport(t.Of("lib"), source, ""));
        var script = Assert.Single(model.Rows, row => row.IsScript);

        Assert.False(script.IsSelected);
        Assert.False(script.CanBulkSelect);
        model.SelectAll();
        Assert.False(script.IsSelected);
        Assert.Empty(model.AcceptedScriptEntries);

        script.IsSelected = true;
        Assert.Contains(script.Entry.Id, model.AcceptedScriptEntries);
        Assert.Equal("\"hello\"", script.CodeSource);
    }

    [Fact]
    public void PowerShellDefinition_IsInvalidUnselectableAndCannotBeImportedOrTrusted()
    {
        using var t = new TempDir();
        string source = t.Of("Native.qrkscript.json");
        File.WriteAllText(source,
            "{\"id\":\"native\",\"name\":\"Native\",\"language\":\"powershell-1\",\"enabled\":true,\"expression\":\"Write-Output 'x'\"}");
        var plan = LibraryPorter.PlanImport(t.Of("lib"), source, "");
        var entry = Assert.Single(plan.Entries);
        var model = new ImportReviewModel(plan);
        var row = Assert.Single(model.Rows);
        using var ledgerDir = new TempDir();
        var ledger = OpenLedger(ledgerDir.Of("trust.json"));

        Assert.Equal(ImportClassification.Invalid, entry.Classification);
        Assert.False(row.CanSelect);
        Assert.False(row.IsSelected);
        var result = LibraryPorter.Import(plan,
            new Dictionary<int, ImportAction> { [entry.Id] = ImportAction.Import },
            new HashSet<int> { entry.Id }, ledger);

        Assert.Equal(ImportOutcomeKind.Invalid, Assert.Single(result.Outcomes).Kind);
        Assert.False(File.Exists(t.Of("lib", "Native.qrkscript.json")));
        Assert.False(ledger.IsTrusted(new ScriptDefinition("native", "Native", "powershell-1", true,
            "Write-Output 'x'")));
    }

    [Fact]
    public void CoreBypass_CannotImportOrTrustUnacceptedScript()
    {
        using var t = new TempDir();
        string source = t.Of("Greeting.qrkscript.json");
        File.WriteAllText(source, Script("greeting", "Greeting", "\"hello\""));
        var plan = LibraryPorter.PlanImport(t.Of("lib"), source, "");
        var entry = Assert.Single(plan.Entries);
        using var ledgerDir = new TempDir();
        var ledger = OpenLedger(ledgerDir.Of("trust.json"));

        var result = LibraryPorter.Import(plan, new Dictionary<int, ImportAction>
        {
            [entry.Id] = ImportAction.Import,
        }, new HashSet<int>(), ledger);

        Assert.Equal(ImportOutcomeKind.Skipped, Assert.Single(result.Outcomes).Kind);
        Assert.False(File.Exists(t.Of("lib", "Greeting.qrkscript.json")));
        Assert.False(ledger.IsTrusted(Definition("greeting", "Greeting", "\"hello\"")));
    }

    [Theory]
    [InlineData(ImportAction.Overwrite, "Greeting.qrkscript.json")]
    [InlineData(ImportAction.KeepBoth, "Greeting (imported).qrkscript.json")]
    public void AcceptedScript_PreservesConflictChoice_AndTrustsInstalledDefinition(
        ImportAction action, string expectedFile)
    {
        using var t = new TempDir();
        string lib = t.Of("lib");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "Greeting.qrkscript.json"),
            Script("old", "Old", "\"old\""));
        string source = t.Of("Greeting.qrkscript.json");
        File.WriteAllText(source, Script("new", "New", "\"new\""));
        var plan = LibraryPorter.PlanImport(lib, source, "");
        var entry = Assert.Single(plan.Entries);
        using var ledgerDir = new TempDir();
        var ledger = OpenLedger(ledgerDir.Of("trust.json"));

        var result = LibraryPorter.Import(plan, new Dictionary<int, ImportAction> { [entry.Id] = action },
            new HashSet<int> { entry.Id }, ledger);

        Assert.Equal(action == ImportAction.Overwrite ? ImportOutcomeKind.Overwritten : ImportOutcomeKind.KeptBoth,
            Assert.Single(result.Outcomes).Kind);
        var installed = ScriptDefinitionFile.Load(Path.Combine(lib, expectedFile));
        Assert.True(installed.Success);
        Assert.True(ledger.IsTrusted(installed.Value));
    }

    [Fact]
    public void ChangedCode_ReplacesOldFingerprintOnlyAfterAcceptedInstall()
    {
        using var t = new TempDir();
        string lib = t.Of("lib");
        Directory.CreateDirectory(lib);
        var oldDefinition = Definition("same-id", "Greeting", "\"old\"");
        var newDefinition = oldDefinition with { Expression = "\"new\"" };
        File.WriteAllText(Path.Combine(lib, "Greeting.qrkscript.json"), JsonSerializer.Serialize(oldDefinition, Json));
        string source = t.Of("Greeting.qrkscript.json");
        File.WriteAllText(source, JsonSerializer.Serialize(newDefinition, Json));
        using var ledgerDir = new TempDir();
        var ledger = OpenLedger(ledgerDir.Of("trust.json"));
        Assert.True(ledger.Trust(oldDefinition).Success);
        var plan = LibraryPorter.PlanImport(lib, source, "");
        var entry = Assert.Single(plan.Entries);

        LibraryPorter.Import(plan, new Dictionary<int, ImportAction> { [entry.Id] = ImportAction.Overwrite },
            new HashSet<int> { entry.Id }, ledger);

        Assert.False(ledger.IsTrusted(oldDefinition));
        Assert.True(ledger.IsTrusted(newDefinition));
    }

    [Fact]
    public void StaleAcceptedScript_IsNeitherWrittenNorTrusted()
    {
        using var t = new TempDir();
        string source = t.Of("Greeting.qrkscript.json");
        var planned = Definition("greeting", "Greeting", "\"planned\"");
        File.WriteAllText(source, JsonSerializer.Serialize(planned, Json));
        var plan = LibraryPorter.PlanImport(t.Of("lib"), source, "");
        var entry = Assert.Single(plan.Entries);
        File.WriteAllText(source, Script("other", "Other", "\"changed\""));
        using var ledgerDir = new TempDir();
        var ledger = OpenLedger(ledgerDir.Of("trust.json"));

        var result = LibraryPorter.Import(plan, new Dictionary<int, ImportAction> { [entry.Id] = ImportAction.Import },
            new HashSet<int> { entry.Id }, ledger);

        Assert.Equal(ImportOutcomeKind.Stale, Assert.Single(result.Outcomes).Kind);
        Assert.False(File.Exists(t.Of("lib", "Greeting.qrkscript.json")));
        Assert.False(ledger.IsTrusted(planned));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrustFailure_RollsBackInstalledScript(bool overwrite)
    {
        using var t = new TempDir();
        string lib = t.Of("lib");
        Directory.CreateDirectory(lib);
        string destination = Path.Combine(lib, "Greeting.qrkscript.json");
        string? original = overwrite ? Script("old", "Old", "\"old\"") : null;
        if (original is not null) File.WriteAllText(destination, original);

        string source = t.Of("Greeting.qrkscript.json");
        File.WriteAllText(source, Script("new", "New", "\"new\""));
        var plan = LibraryPorter.PlanImport(lib, source, "");
        var entry = Assert.Single(plan.Entries);
        string blockedParent = t.Of("trust-parent");
        File.WriteAllText(blockedParent, "not a directory");
        var opened = ScriptTrustLedger.Open(Path.Combine(blockedParent, "trust.json"));

        var result = LibraryPorter.Import(plan,
            new Dictionary<int, ImportAction>
            {
                [entry.Id] = overwrite ? ImportAction.Overwrite : ImportAction.Import,
            },
            new HashSet<int> { entry.Id }, opened.Value!);

        Assert.Equal(ImportOutcomeKind.Failed, Assert.Single(result.Outcomes).Kind);
        if (original is null) Assert.False(File.Exists(destination));
        else Assert.Equal(original, File.ReadAllText(destination));
        Assert.Empty(Directory.EnumerateFiles(lib, "*.rollback-*"));
        Assert.Empty(Directory.EnumerateFiles(lib, "*.tmp-*"));
    }

    [Fact]
    public void ScriptTraversal_IsUnsafe_AndNeverWritesOutsideLibrary()
    {
        using var t = new TempDir();
        string source = t.Of("unsafe.zip");
        MakeZip(source, ("../../evil.qrkscript.json", Script("evil", "Evil", "\"x\"")));

        var plan = LibraryPorter.PlanImport(t.Of("lib"), source, "");

        Assert.Equal(ImportClassification.Unsafe, Assert.Single(plan.Entries).Classification);
        Assert.False(File.Exists(t.Of("evil.qrkscript.json")));
    }

    [Fact]
    public void ScriptDestinationThroughReparsePoint_IsUnsafe()
    {
        using var t = new TempDir();
        string root = t.Of("lib");
        string outside = t.Of("outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try { Directory.CreateSymbolicLink(Path.Combine(root, "Linked"), outside); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        { return; }
        string source = t.Of("Greeting.qrkscript.json");
        File.WriteAllText(source, Script("greeting", "Greeting", "\"hello\""));

        var entry = Assert.Single(LibraryPorter.PlanImport(root, source, "Linked").Entries);

        Assert.Equal(ImportClassification.Unsafe, entry.Classification);
        Assert.False(File.Exists(Path.Combine(outside, "Greeting.qrkscript.json")));
    }

    [Fact]
    public void ExportLibrary_IncludesDefinitions_ButNeverTrustLedger()
    {
        using var t = new TempDir();
        string lib = t.Of("lib");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "Greeting.qrkscript.json"),
            Script("greeting", "Greeting", "\"hello\""));
        File.WriteAllText(Path.Combine(lib, "script-trust.json"), "{\"trusted\":true}");
        using var stage = new TempDir();
        string zipPath = stage.Of("library.zip");

        LibraryPorter.ExportLibrary(lib, zipPath);

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, entry => entry.FullName == "Greeting.qrkscript.json");
        Assert.DoesNotContain(zip.Entries, entry =>
            entry.FullName.EndsWith("script-trust.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReviewUi_ShowsLabeledReadOnlyMonospacedExactSource()
    {
        var document = XDocument.Load(TestFiles.RepoAsset("src/QuickReplyKit/Views/ImportPreviewWindow.xaml"));
        var source = document.Descendants().Single(element =>
            (string?)element.Attribute("Text") == "{Binding CodeSource, Mode=OneWay}");

        Assert.Equal("{Binding CodeSource, Mode=OneWay}", (string?)source.Attribute("Text"));
        Assert.Equal("True", (string?)source.Attribute("IsReadOnly"));
        Assert.Equal("Consolas", (string?)source.Attribute("FontFamily"));
        Assert.Contains(document.Descendants(), element =>
            (string?)element.Attribute("Text") == "{Binding ScriptWarning}");
        Assert.Equal("{Binding SourceAutomationName}",
            (string?)source.Attribute("AutomationProperties.Name"));
    }

    private static ScriptTrustLedger OpenLedger(string path)
    {
        var opened = ScriptTrustLedger.Open(path);
        Assert.True(opened.Success, opened.Error);
        return opened.Value!;
    }

    private static ScriptDefinition Definition(string id, string name, string expression) =>
        new(id, name, ScriptDefinitionFile.Language, true, expression);

    private static string Script(string id, string name, string expression) =>
        JsonSerializer.Serialize(Definition(id, name, expression), Json);

    private static void MakeZip(string path, params (string Name, string Content)[] entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
    }
}
