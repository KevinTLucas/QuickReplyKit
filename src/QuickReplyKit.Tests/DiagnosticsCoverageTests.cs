using System.IO;
using QuickReplyKit.Core.Diagnostics;
using QuickReplyKit.Core.ImportExport;
using QuickReplyKit.Core.Library;
using QuickReplyKit.Core.Search;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.Core.Templates;

namespace QuickReplyKit.Tests;

[CollectionDefinition("Diagnostics", DisableParallelization = true)]
public sealed class DiagnosticsCollection;

[Collection("Diagnostics")]
public class DiagnosticsCoverageTests
{
    [Fact]
    public void WatcherDelivery_IsRecordedWithoutAPath()
    {
        using var temp = new TempDir();
        string library = temp.Of("library");
        string logs = temp.Of("logs");
        Directory.CreateDirectory(library);
        using var delivered = new ManualResetEventSlim();

        Diag.Init(logs, verbose: true);
        try
        {
            using var watcher = new LibraryWatcher(library, 50);
            watcher.Changed += delivered.Set;
            File.WriteAllText(Path.Combine(library, "change.json"), "{}");
            Assert.True(delivered.Wait(TimeSpan.FromSeconds(5)));
        }
        finally { Diag.Shutdown(); }

        string text = File.ReadAllText(Path.Combine(logs, "quickreplykit.log"));
        Assert.Contains("[LibraryWatcher/Watcher] activity delivered=true", text);
        Assert.DoesNotContain(library, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UiAndPasteWorkflows_KeepRequiredStageEvents()
    {
        string paste = File.ReadAllText(TestFiles.RepoAsset("src/QuickReplyKit/Services/InsertService.cs"));
        foreach (string stage in new[] { "target present=", "snapshot captured=", "focus result=",
                     "transientWrite=", "input send=", "clipboard restore=", "completion result=" })
            Assert.Contains(stage, paste, StringComparison.Ordinal);

        string theme = File.ReadAllText(TestFiles.RepoAsset("src/QuickReplyKit/Services/ThemeManager.cs"));
        Assert.Contains("\"ThemeManager\", \"Theme\"", theme, StringComparison.Ordinal);

        string actions = File.ReadAllText(TestFiles.RepoAsset("src/QuickReplyKit/Views/LauncherWindow.xaml.cs"));
        foreach (string failure in new[] { "read clipboard failed", "open response folder failed",
                     "delete response failed", "delete folder failed", "import preview failed", "import backup failed",
                     "import review failed safely", "import execution failed safely", "import workflow failed safely" })
            Assert.Contains(failure, actions, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowLogs_ContainMetrics_ButNeverSensitiveCanary()
    {
        using var temp = new TempDir();
        string canary = "QRK-PRIVATE-" + Guid.NewGuid().ToString("N");
        string logs = temp.Of("logs");
        string library = temp.Of(canary, "library");
        string source = Path.Combine(library, canary + ".json");
        string export = temp.Of(canary, canary + ".json");
        string imported = temp.Of(canary + "-imported");
        TestFiles.WriteResponse(source, $"<p>{canary}</p>", canary, tags: new[] { canary });

        Diag.Init(logs, verbose: true);
        try
        {
            var snapshot = LibraryScanner.Scan(library);
            ResponseSearch.Rank(snapshot.AllResponses, canary, canary);
            TemplateEngine.Resolve($"{{{{{canary}}}}}", _ => $"<p>{canary}</p>", DateTimeOffset.Now);
            using (var watcher = new LibraryWatcher(library)) { }
            LibraryPorter.Export(ResponseFile.Load(source, library), export);
            var plan = LibraryPorter.PlanImport(imported, export, canary);
            LibraryPorter.Import(plan);
            Diag.Error("DiagnosticsTest", "Privacy", "canary exception", new IOException(canary));
        }
        finally
        {
            Diag.Shutdown();
        }

        string text = File.ReadAllText(Path.Combine(logs, "quickreplykit.log"));
        Assert.DoesNotContain(canary, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[LibraryScanner/Scan]", text);
        Assert.Contains("[ResponseSearch/Search]", text);
        Assert.Contains("fuzzyTokens=", text);
        Assert.Contains("[TemplateEngine/Template]", text);
        Assert.Contains("resolvedIncludes=", text);
        Assert.Contains("[LibraryWatcher/Watcher]", text);
        Assert.Contains("[LibraryPorter/Export]", text);
        Assert.Contains("[LibraryPorter/ImportPlan]", text);
        Assert.Contains("[LibraryPorter/ImportExecution]", text);
    }
}
