using System.IO;
using System.Threading;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Scripts;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.Core.Templates;
using QuickReplyKit.Services;
using QuickReplyKit.ViewModels;

namespace QuickReplyKit.Tests;

public class ScriptRuntimeTests
{
    private static readonly DateTimeOffset Morning =
        new(2026, 9, 7, 9, 5, 0, TimeSpan.Zero);

    private sealed class FakeInsert : IInsertService
    {
        public readonly List<(string Mode, ResponseItem Item)> Calls = new();
        public void InsertRich(ResponseItem item) => Calls.Add(("rich", item));
        public void InsertPlain(ResponseItem item) => Calls.Add(("plain", item));
        public void Copy(ResponseItem item) => Calls.Add(("copy", item));
    }

    private static ScriptTrustLedger Ledger(TempDir temp) =>
        AssertSuccess(ScriptTrustLedger.Open(temp.Of("trust.json")));

    private static ScriptDefinition WriteScript(TempDir temp, string name, string expression,
        bool enabled = true, string? id = null, string? file = null)
    {
        var definition = new ScriptDefinition(id ?? Guid.NewGuid().ToString("N"), name,
            ScriptDefinitionFile.Language, enabled, expression);
        string path = temp.Of("lib", file ?? name + ScriptDefinitionFile.Extension);
        Assert.True(ScriptDefinitionFile.Save(path, definition).Success);
        return definition;
    }

    private static T AssertSuccess<T>(ScriptValueResult<T> result) where T : class
    {
        Assert.True(result.Success, result.Error);
        return Assert.IsType<T>(result.Value);
    }

    [Fact]
    public void Catalog_TrustedScriptEvaluates_AndSourceChangeInvalidatesTrust()
    {
        using var temp = new TempDir();
        var ledger = Ledger(temp);
        var definition = WriteScript(temp, "Salutation", "concat(\"Hi at \" , time())");
        Assert.True(ledger.Trust(definition).Success);

        var catalog = ScriptCatalog.Load(temp.Of("lib"), ledger);
        Assert.Equal("Hi at 09:05", catalog.Evaluate("salutation", Morning).Output);

        var changed = definition with { Expression = "\"changed\"" };
        Assert.True(ScriptDefinitionFile.Save(temp.Of("lib", "Salutation" + ScriptDefinitionFile.Extension), changed).Success);
        catalog = ScriptCatalog.Load(temp.Of("lib"), ledger);
        Assert.Equal("[script untrusted: Salutation]", catalog.Evaluate("Salutation", Morning).Output);
    }

    [Fact]
    public void Catalog_InvalidDisabledUntrustedMissingAndDuplicate_FailClosed()
    {
        using var temp = new TempDir();
        var ledger = Ledger(temp);
        WriteScript(temp, "Disabled", "\"no\"", enabled: false);
        WriteScript(temp, "Untrusted", "\"no\"");
        WriteScript(temp, "Same", "\"one\"", file: "one" + ScriptDefinitionFile.Extension);
        WriteScript(temp, "same", "\"two\"", file: "two" + ScriptDefinitionFile.Extension);
        Directory.CreateDirectory(temp.Of("lib"));
        File.WriteAllText(temp.Of("lib", "Broken" + ScriptDefinitionFile.Extension), "{broken");

        var catalog = ScriptCatalog.Load(temp.Of("lib"), ledger);

        Assert.Equal("[script disabled: Disabled]", catalog.Evaluate("Disabled", Morning).Output);
        Assert.Equal("[script untrusted: Untrusted]", catalog.Evaluate("Untrusted", Morning).Output);
        Assert.Equal("[script duplicate: SAME]", catalog.Evaluate("SAME", Morning).Output);
        Assert.Equal("[script invalid: Broken]", catalog.Evaluate("Broken", Morning).Output);
        Assert.Equal("[script missing: Missing]", catalog.Evaluate("Missing", Morning).Output);
    }

    [Fact]
    public void ScannerAndLibraryViews_ExcludeScriptDefinitions()
    {
        using var temp = new TempDir();
        TestFiles.WriteResponse(temp.Of("lib", "Visible.json"), favorite: true);
        WriteScript(temp, "Invisible Script", "\"secret-search-term\"");

        using var library = new LibraryService(temp.Of("lib"), recentPath: temp.Of("recent.json"),
            watch: false, scriptTrust: Ledger(temp));

        Assert.Equal("Visible", Assert.Single(library.Snapshot.AllResponses).Name);
        Assert.Equal("Visible", Assert.Single(library.Favorites()).Name);
        Assert.Empty(library.Recent());
        Assert.Empty(library.Search("Invisible Script"));
        Assert.Empty(library.Search("secret-search-term"));
    }

    [Fact]
    public void Template_ScriptsExecuteOnlyInTextNodes_IncludingThroughResponseIncludes()
    {
        int calls = 0;
        ScriptEvaluationResult Evaluate(string _, DateTimeOffset __)
        {
            calls++;
            return new(true, "SAFE", null);
        }
        string? Lookup(string name) => name == "AttrInclude" ? "{{script:Clock}}" : null;

        string html = "<a href=\"{{script:Clock}}\" title=\"a>b {{AttrInclude}}\">{{script:Clock}}</a>";
        string result = TemplateEngine.Resolve(html, Lookup, Morning, Evaluate);

        Assert.Equal("<a href=\"{{script:Clock}}\" title=\"a>b {{script:Clock}}\">SAFE</a>", result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Template_UsesOneClock_AndStopsAtSixtyFourScriptCalls()
    {
        var clocks = new List<DateTimeOffset>();
        ScriptEvaluationResult Evaluate(string _, DateTimeOffset now)
        {
            clocks.Add(now);
            return new(true, "x", null);
        }
        string html = string.Concat(Enumerable.Repeat("{{script:Clock}}", TemplateEngine.MaxScriptCalls + 1));

        string result = TemplateEngine.Resolve(html, _ => null, Morning, Evaluate);

        Assert.Equal(TemplateEngine.MaxScriptCalls, clocks.Count);
        Assert.All(clocks, value => Assert.Equal(Morning, value));
        Assert.Equal(TemplateEngine.MaxScriptCalls, result.Count(c => c == 'x'));
        Assert.EndsWith("[scripts: too many]", result);
    }

    [Fact]
    public void Template_AggregateOutputLimitReturnsVisibleMarker()
    {
        string oversized = new('x', TemplateEngine.MaxResolvedChars + 1);

        Assert.Equal("[template error: output-limit]",
            TemplateEngine.Resolve(oversized, _ => null, Morning));
    }

    [Fact]
    public void LibraryResolver_TrustedScriptRunsAndUntrustedScriptShowsMarker()
    {
        using var temp = new TempDir();
        var ledger = Ledger(temp);
        var trusted = WriteScript(temp, "Clock", "time()");
        WriteScript(temp, "NoTrust", "\"hidden\"");
        Assert.True(ledger.Trust(trusted).Success);
        TestFiles.WriteResponse(temp.Of("lib", "Response.json"),
            html: "<p>{{script:Clock}} / {{script:NoTrust}}</p>");

        using var library = new LibraryService(temp.Of("lib"), recentPath: temp.Of("recent.json"),
            watch: false, scriptTrust: ledger);

        Assert.Equal("<p>09:05 / [script untrusted: NoTrust]</p>",
            library.ResolveTemplate(library.Snapshot.ValidResponses.Single().Html, Morning));
    }

    [Fact]
    public void LauncherPreviewRichPlainCopyAndCompose_UseSharedScriptResolver()
    {
        RunSta(() =>
        {
            using var temp = new TempDir();
            var ledger = Ledger(temp);
            var script = WriteScript(temp, "Clock", "time()");
            Assert.True(ledger.Trust(script).Success);
            TestFiles.WriteResponse(temp.Of("lib", "Response.json"), html: "<p>{{script:Clock}}</p>");
            using var library = new LibraryService(temp.Of("lib"), recentPath: temp.Of("recent.json"),
                watch: false, scriptTrust: ledger);
            var insert = new FakeInsert();
            var vm = new LauncherViewModel(library, insert,
                new AppConfig { DefaultPasteMode = PasteMode.RichText }, () => Morning);

            Assert.Contains("09:05", new System.Windows.Documents.TextRange(
                vm.PreviewDocument.ContentStart, vm.PreviewDocument.ContentEnd).Text);
            vm.TriggerDefaultInsert();
            vm.TriggerPlainInsert();
            vm.CopyCommand.Execute(null);
            vm.InsertComposed("<p>{{script:Clock}}</p>", plain: false);

            Assert.Equal(new[] { "rich", "plain", "copy", "rich" }, insert.Calls.Select(x => x.Mode));
            Assert.All(insert.Calls, call => Assert.Equal("<p>09:05</p>", call.Item.Html));
        });
    }

    [Fact]
    public void InstantResponseHotkey_UsesSharedScriptResolverWithoutOpeningLauncher()
    {
        RunSta(() =>
        {
            using var temp = new TempDir();
            var ledger = Ledger(temp);
            var script = WriteScript(temp, "Clock", "time()");
            Assert.True(ledger.Trust(script).Success);
            TestFiles.WriteResponse(temp.Of("lib", "Response.json"), html: "<p>{{script:Clock}}</p>");
            using var library = new LibraryService(temp.Of("lib"), recentPath: temp.Of("recent.json"),
                watch: false, scriptTrust: ledger);
            var insert = new FakeInsert();
            using var hotkeys = new HotkeyService(new AppConfig(), library, insert, () => Morning);

            hotkeys.InstantPaste("Response.json", plain: true);

            var call = Assert.Single(insert.Calls);
            Assert.Equal("plain", call.Mode);
            Assert.Equal("<p>09:05</p>", call.Item.Html);
            Assert.Equal("Response", Assert.Single(library.Recent()).Name);
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }
}
