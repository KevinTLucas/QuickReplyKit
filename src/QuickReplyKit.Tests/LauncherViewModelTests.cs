using System.IO;
using System.Threading;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.Services;
using QuickReplyKit.ViewModels;

namespace QuickReplyKit.Tests;

/// <summary>
/// VM-level tests for <see cref="LauncherViewModel"/>: keyboard selection, scope vs global
/// search, the three insert actions through a fake <see cref="IInsertService"/>, favorite/recent
/// mutations, and live-update selection stability. The VM builds a <see cref="System.Windows.Documents.FlowDocument"/>
/// in its constructor (a DispatcherObject), so every body runs on an STA thread.
/// </summary>
public class LauncherViewModelTests
{
    private static void Sta(Action test)
    {
        Exception? captured = null;
        var t = new Thread(() =>
        {
            try { test(); }
            catch (Exception e) { captured = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        t.Join();
        if (captured != null) throw new Exception("STA test body threw", captured);
    }

    private sealed class FakeInsertService : IInsertService
    {
        public readonly List<(string Mode, ResponseItem Item)> Calls = new();
        public void InsertRich(ResponseItem item) => Calls.Add(("rich", item));
        public void InsertPlain(ResponseItem item) => Calls.Add(("plain", item));
        public void Copy(ResponseItem item) => Calls.Add(("copy", item));
    }

    private static LibraryService NewService(TempDir dir, bool watch = false) =>
        new(dir.Of("lib"), seedSourceDir: null, recentPath: dir.Of("recent.json"), watch: watch);

    private static LauncherViewModel NewVm(LibraryService svc, FakeInsertService insert,
        PasteMode mode = PasteMode.RichText) =>
        new(svc, insert, new AppConfig { DefaultPasteMode = mode });

    private static IEnumerable<NavNodeVm> Flatten(IEnumerable<NavNodeVm> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            foreach (var c in Flatten(n.Children)) yield return c;
        }
    }

    private static NavNodeVm Nav(LauncherViewModel vm, NavScope scope, string path = "") =>
        Flatten(vm.NavRoots).First(n => n.Scope == scope && (scope != NavScope.Category || n.CategoryPath == path));

    /// <summary>A small library with a root file, two categories (one nested), and one invalid file.</summary>
    private static void SeedTree(TempDir dir)
    {
        TestFiles.WriteResponse(dir.Of("lib", "Alpha.json"), app: "Zulu");
        TestFiles.WriteResponse(dir.Of("lib", "Cat1", "Beta.json"));
        TestFiles.WriteResponse(dir.Of("lib", "Cat1", "Sub", "Gamma.json"));
        TestFiles.WriteResponse(dir.Of("lib", "Cat2", "Delta.json"));
        File.WriteAllText(dir.Of("lib", "Cat1", "Broken.json"), "{ not json");
    }

    // --- MoveSelection ------------------------------------------------------

    [Fact]
    public void MoveSelection_ClampsAtEnds_AndMoves()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            TestFiles.WriteResponse(dir.Of("lib", "A.json"));
            TestFiles.WriteResponse(dir.Of("lib", "B.json"));
            TestFiles.WriteResponse(dir.Of("lib", "C.json"));
            using var svc = NewService(dir);
            var vm = NewVm(svc, new FakeInsertService());

            Assert.Equal("A", vm.SelectedResult!.Name);        // first row selected on load
            vm.MoveSelection(-1);
            Assert.Equal("A", vm.SelectedResult!.Name);        // clamp at top
            vm.MoveSelection(1);
            Assert.Equal("B", vm.SelectedResult!.Name);
            vm.MoveSelection(1);
            Assert.Equal("C", vm.SelectedResult!.Name);
            vm.MoveSelection(1);
            Assert.Equal("C", vm.SelectedResult!.Name);        // clamp at bottom
            vm.MoveSelection(-1);
            Assert.Equal("B", vm.SelectedResult!.Name);
        });
    }

    [Fact]
    public void MoveSelection_OnEmptyResults_IsNoOp()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            Directory.CreateDirectory(dir.Of("lib"));
            using var svc = NewService(dir);
            var vm = NewVm(svc, new FakeInsertService());

            Assert.Empty(vm.Results);
            Assert.Null(vm.SelectedResult);
            vm.MoveSelection(1);
            vm.MoveSelection(-1);
            Assert.Null(vm.SelectedResult);                    // no throw, still nothing selected
            Assert.True(vm.IsEmptyState);
        });
    }

    // --- Scope vs global search --------------------------------------------

    [Fact]
    public void EmptyQuery_AllScope_ShowsValidByName_ThenInvalid()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            SeedTree(dir);
            using var svc = NewService(dir);
            var vm = NewVm(svc, new FakeInsertService());   // lands on All

            // Valid responses by name first, then invalid ones (surfaced so they can be fixed).
            Assert.Equal(new[] { "Alpha", "Beta", "Delta", "Gamma", "Broken" },
                vm.Results.Select(r => r.Name).ToArray());
        });
    }

    [Fact]
    public void AllScope_SurfacesRootInvalidFile_ButExcludesItFromPaste()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            TestFiles.WriteResponse(dir.Of("lib", "Good.json"));
            File.WriteAllText(dir.Of("lib", "RootBroken.json"), "{ not json");   // invalid at library ROOT
            using var svc = NewService(dir);
            var insert = new FakeInsertService();
            var vm = NewVm(svc, insert);   // lands on All

            // The root-level invalid file now appears in "All Responses" (BACKLOG item).
            var broken = vm.Results.FirstOrDefault(r => r.Name == "RootBroken");
            Assert.NotNull(broken);
            Assert.False(broken!.IsValid);
            Assert.True(broken.ShowStatusGlyph);               // ⚠ marker

            // …but it is not paste-able: selecting it disables all three insert actions.
            vm.SelectedResult = broken;
            Assert.False(vm.HasValidSelection);
            Assert.False(vm.PasteCommand.CanExecute(null));
            Assert.False(vm.PastePlainCommand.CanExecute(null));
            Assert.False(vm.CopyCommand.CanExecute(null));

            vm.TriggerDefaultInsert();
            vm.TriggerPlainInsert();
            Assert.Empty(insert.Calls);                        // nothing inserted
        });
    }

    [Fact]
    public void CategoryScope_IncludesDescendants_AndInvalidMarker()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            SeedTree(dir);
            using var svc = NewService(dir);
            var vm = NewVm(svc, new FakeInsertService());

            vm.SelectedNav = Nav(vm, NavScope.Category, "Cat1");

            var names = vm.Results.Select(r => r.Name).ToArray();
            Assert.Contains("Beta", names);      // directly in Cat1
            Assert.Contains("Gamma", names);     // in Cat1/Sub (descendant)
            Assert.Contains("Broken", names);    // invalid still surfaced in a category view
            Assert.DoesNotContain("Delta", names);  // Cat2
            Assert.DoesNotContain("Alpha", names);  // root
        });
    }

    [Fact]
    public void NonEmptyQuery_UsesGlobalSearch_IgnoringNavScope()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            SeedTree(dir);
            using var svc = NewService(dir);
            var vm = NewVm(svc, new FakeInsertService());

            vm.SelectedNav = Nav(vm, NavScope.Category, "Cat2");  // scope that does NOT contain Beta
            vm.Query = "beta";                                     // global search wins

            Assert.Contains(vm.Results, r => r.Name == "Beta");
        });
    }

    [Fact]
    public void EmptyingQuery_RestoresSelectedNavScope()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            SeedTree(dir);
            using var svc = NewService(dir);
            var vm = NewVm(svc, new FakeInsertService());

            vm.SelectedNav = Nav(vm, NavScope.Category, "Cat2");
            vm.Query = "beta";
            vm.Query = "";                                         // back to the Cat2 scope

            Assert.Equal(new[] { "Delta" }, vm.Results.Select(r => r.Name).ToArray());
        });
    }

    [Fact]
    public void FavoritesScope_ShowsOnlyFavorites()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            TestFiles.WriteResponse(dir.Of("lib", "Fav.json"), favorite: true);
            TestFiles.WriteResponse(dir.Of("lib", "Plain.json"), favorite: false);
            using var svc = NewService(dir);
            var vm = NewVm(svc, new FakeInsertService());

            vm.SelectedNav = Nav(vm, NavScope.Favorites);

            Assert.Equal(new[] { "Fav" }, vm.Results.Select(r => r.Name).ToArray());
        });
    }

    [Fact]
    public void RecentScope_ShowsUsedItems_MostRecentFirst()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            TestFiles.WriteResponse(dir.Of("lib", "A.json"));
            TestFiles.WriteResponse(dir.Of("lib", "B.json"));
            using var svc = NewService(dir);
            var vm = NewVm(svc, new FakeInsertService());

            svc.MarkUsed(svc.Snapshot.AllResponses.First(r => r.Name == "A"));
            svc.MarkUsed(svc.Snapshot.AllResponses.First(r => r.Name == "B"));
            vm.SelectedNav = Nav(vm, NavScope.Recent);

            Assert.Equal(new[] { "B", "A" }, vm.Results.Select(r => r.Name).ToArray());
        });
    }

    // --- Insert actions -----------------------------------------------------

    [Fact]
    public void DefaultInsert_RichMode_CallsInsertRich_MarksUsed_AndHides()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            TestFiles.WriteResponse(dir.Of("lib", "A.json"));
            using var svc = NewService(dir);
            var insert = new FakeInsertService();
            var vm = NewVm(svc, insert, PasteMode.RichText);
            bool hidden = false;
            vm.RequestHide += () => hidden = true;

            vm.TriggerDefaultInsert();

            var call = Assert.Single(insert.Calls);
            Assert.Equal("rich", call.Mode);
            Assert.Equal("A", call.Item.Name);
            Assert.Contains(svc.Recent(), r => r.Name == "A");
            Assert.True(hidden);
        });
    }

    [Fact]
    public void DefaultInsert_PlainMode_CallsInsertPlain()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            TestFiles.WriteResponse(dir.Of("lib", "A.json"));
            using var svc = NewService(dir);
            var insert = new FakeInsertService();
            var vm = NewVm(svc, insert, PasteMode.PlainText);

            vm.TriggerDefaultInsert();

            Assert.Equal("plain", Assert.Single(insert.Calls).Mode);
        });
    }

    [Fact]
    public void TriggerPlainInsert_AlwaysCallsInsertPlain()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            TestFiles.WriteResponse(dir.Of("lib", "A.json"));
            using var svc = NewService(dir);
            var insert = new FakeInsertService();
            var vm = NewVm(svc, insert, PasteMode.RichText);   // default is rich, but plain is forced

            vm.TriggerPlainInsert();

            Assert.Equal("plain", Assert.Single(insert.Calls).Mode);
        });
    }

    [Fact]
    public void Copy_CallsCopy_MarksUsed_ButDoesNotHide()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            TestFiles.WriteResponse(dir.Of("lib", "A.json"));
            using var svc = NewService(dir);
            var insert = new FakeInsertService();
            var vm = NewVm(svc, insert);
            bool hidden = false;
            vm.RequestHide += () => hidden = true;

            Assert.True(vm.CopyCommand.CanExecute(null));
            vm.CopyCommand.Execute(null);

            Assert.Equal("copy", Assert.Single(insert.Calls).Mode);
            Assert.Contains(svc.Recent(), r => r.Name == "A");
            Assert.False(hidden);                              // Copy leaves the launcher open
        });
    }

    [Fact]
    public void InvalidSelection_InsertsNothing_AndCommandsDisabled()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            Directory.CreateDirectory(dir.Of("lib", "Cat1"));
            File.WriteAllText(dir.Of("lib", "Cat1", "Broken.json"), "{ not json");
            using var svc = NewService(dir);
            var insert = new FakeInsertService();
            var vm = NewVm(svc, insert);

            // The category view surfaces the invalid row (⚠); select it.
            vm.SelectedNav = Nav(vm, NavScope.Category, "Cat1");
            var broken = vm.Results.FirstOrDefault(r => r.Name == "Broken");
            vm.SelectedResult = broken;

            Assert.NotNull(broken);
            Assert.False(vm.HasValidSelection);
            Assert.False(vm.PasteCommand.CanExecute(null));
            Assert.False(vm.PastePlainCommand.CanExecute(null));
            Assert.False(vm.CopyCommand.CanExecute(null));

            vm.TriggerDefaultInsert();
            vm.TriggerPlainInsert();
            Assert.Empty(insert.Calls);
        });
    }

    [Fact]
    public void NullSelection_InsertsNothing()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            Directory.CreateDirectory(dir.Of("lib"));
            using var svc = NewService(dir);
            var insert = new FakeInsertService();
            var vm = NewVm(svc, insert);

            Assert.Null(vm.SelectedResult);
            vm.TriggerDefaultInsert();
            vm.TriggerPlainInsert();
            Assert.False(vm.CopyCommand.CanExecute(null));
            Assert.Empty(insert.Calls);
        });
    }

    // --- Favorite toggle ----------------------------------------------------

    [Fact]
    public void ToggleFavorite_FromRow_PersistsToDisk()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            TestFiles.WriteResponse(dir.Of("lib", "A.json"), favorite: false);
            using var svc = NewService(dir);
            var vm = NewVm(svc, new FakeInsertService());

            var row = vm.Results.Single(r => r.Name == "A");
            Assert.False(row.IsFavorite);
            row.ToggleFavoriteCommand.Execute(null);

            Assert.True(row.IsFavorite);
            // Persisted: a fresh service over the same folder sees the favorite.
            using var reopened = NewService(dir);
            Assert.True(reopened.Snapshot.AllResponses.Single().Favorite);
        });
    }

    [Fact]
    public void ToggleFavorite_InFavoritesView_DropsTheRow()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            TestFiles.WriteResponse(dir.Of("lib", "A.json"), favorite: true);
            using var svc = NewService(dir);
            var vm = NewVm(svc, new FakeInsertService());

            vm.SelectedNav = Nav(vm, NavScope.Favorites);
            var row = Assert.Single(vm.Results);
            row.ToggleFavoriteCommand.Execute(null);           // unfavorite

            Assert.Empty(vm.Results);                          // drops out of the Favorites scope
        });
    }

    // --- Live update / selection stability ----------------------------------

    [Fact]
    public void OnLibraryUpdated_AddsFile_PreservesResultSelection()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            TestFiles.WriteResponse(dir.Of("lib", "A.json"));
            TestFiles.WriteResponse(dir.Of("lib", "B.json"));
            TestFiles.WriteResponse(dir.Of("lib", "C.json"));
            using var svc = NewService(dir);
            var vm = NewVm(svc, new FakeInsertService());

            vm.SelectedResult = vm.Results.Single(r => r.Name == "B");

            TestFiles.WriteResponse(dir.Of("lib", "D.json"));
            svc.Reload();                                      // watcher does this before Updated
            vm.OnLibraryUpdated();

            Assert.Contains(vm.Results, r => r.Name == "D");   // new file appears
            Assert.Equal("B", vm.SelectedResult!.Name);        // selection preserved
        });
    }

    [Fact]
    public void OnLibraryUpdated_PreservesNavScope_AndKeepsSingleSelectedNode()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            SeedTree(dir);
            using var svc = NewService(dir);
            var vm = NewVm(svc, new FakeInsertService());

            vm.SelectedNav = Nav(vm, NavScope.Category, "Cat1");

            TestFiles.WriteResponse(dir.Of("lib", "Cat1", "Epsilon.json"));
            svc.Reload();
            vm.OnLibraryUpdated();

            Assert.Equal(NavScope.Category, vm.SelectedNav!.Scope);
            Assert.Equal("Cat1", vm.SelectedNav!.CategoryPath);
            // Regression: exactly one nav node may be flagged selected after a refresh.
            Assert.Equal(1, Flatten(vm.NavRoots).Count(n => n.IsSelected));
            Assert.Contains(vm.Results, r => r.Name == "Epsilon");
        });
    }

    [Fact]
    public void OnLibraryUpdated_WhenSelectedItemDeleted_FallsBackToFirst()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            TestFiles.WriteResponse(dir.Of("lib", "A.json"));
            TestFiles.WriteResponse(dir.Of("lib", "B.json"));
            using var svc = NewService(dir);
            var vm = NewVm(svc, new FakeInsertService());

            vm.SelectedResult = vm.Results.Single(r => r.Name == "B");
            File.Delete(dir.Of("lib", "B.json"));
            svc.Reload();
            vm.OnLibraryUpdated();

            Assert.DoesNotContain(vm.Results, r => r.Name == "B");
            Assert.Equal("A", vm.SelectedResult!.Name);        // graceful fallback, no throw
        });
    }

    // --- Template resolution (Phase 9) --------------------------------------

    /// <summary>9am fixed clock → {{greeting}} = "Good morning".</summary>
    private static readonly Func<DateTimeOffset> MorningClock =
        () => new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    private static string DocText(System.Windows.Documents.FlowDocument doc) =>
        new System.Windows.Documents.TextRange(doc.ContentStart, doc.ContentEnd).Text;

    [Fact]
    public void Insert_ResolvesVariablesAndIncludes_BeforeReachingInsertService()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            TestFiles.WriteResponse(dir.Of("lib", "Greeter.json"),
                html: "<p>{{greeting}}, see {{Welcome}}</p>");
            TestFiles.WriteResponse(dir.Of("lib", "Welcome.json"), html: "<p>the welcome text</p>");
            using var svc = NewService(dir);
            var insert = new FakeInsertService();
            var vm = new LauncherViewModel(svc, insert, new AppConfig { DefaultPasteMode = PasteMode.RichText }, MorningClock);

            vm.SelectedResult = vm.Results.Single(r => r.Name == "Greeter");
            vm.TriggerDefaultInsert();

            var call = Assert.Single(insert.Calls);
            Assert.Equal("rich", call.Mode);
            // {{greeting}} filled in, {{Welcome}} include expanded, no tokens left.
            Assert.Contains("Good morning", call.Item.Html);
            Assert.Contains("the welcome text", call.Item.Html);
            Assert.DoesNotContain("{{", call.Item.Html);
            // Plain text re-derived from the resolved HTML via HtmlText.ToPlainText.
            Assert.Contains("Good morning", call.Item.PlainText);
            Assert.Contains("the welcome text", call.Item.PlainText);
            // The stored item is never mutated.
            Assert.Equal("<p>{{greeting}}, see {{Welcome}}</p>",
                svc.Snapshot.AllResponses.Single(r => r.Name == "Greeter").Html);
        });
    }

    [Fact]
    public void PlainInsert_And_Copy_AlsoResolveTokens()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            TestFiles.WriteResponse(dir.Of("lib", "A.json"), html: "<p>Today is {{day}}</p>");
            using var svc = NewService(dir);
            var insert = new FakeInsertService();
            var vm = new LauncherViewModel(svc, insert, new AppConfig(), MorningClock);  // 2026-01-01 = Thursday

            vm.SelectedResult = vm.Results.Single();
            vm.TriggerPlainInsert();
            vm.CopyCommand.Execute(null);

            Assert.Equal(2, insert.Calls.Count);
            Assert.All(insert.Calls, c => Assert.Contains("Thursday", c.Item.PlainText));
            Assert.All(insert.Calls, c => Assert.DoesNotContain("{{", c.Item.Html));
        });
    }

    [Fact]
    public void Preview_RendersResolvedText_AndSetsVariablesHint()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            TestFiles.WriteResponse(dir.Of("lib", "Greeter.json"),
                html: "<p>{{greeting}}! {{Welcome}}</p>");
            TestFiles.WriteResponse(dir.Of("lib", "Welcome.json"), html: "<p>Thanks for writing.</p>");
            TestFiles.WriteResponse(dir.Of("lib", "Plain.json"), html: "<p>No tokens here.</p>");
            using var svc = NewService(dir);
            var vm = new LauncherViewModel(svc, new FakeInsertService(), new AppConfig(), MorningClock);

            vm.SelectedResult = vm.Results.Single(r => r.Name == "Greeter");
            Assert.True(vm.SelectionHasVariables);                       // hint shows
            var text = DocText(vm.PreviewDocument);
            Assert.Contains("Good morning", text);                      // resolved in the preview
            Assert.Contains("Thanks for writing.", text);               // include expanded
            Assert.DoesNotContain("{{", text);

            vm.SelectedResult = vm.Results.Single(r => r.Name == "Plain");
            Assert.False(vm.SelectionHasVariables);                     // no hint without tokens
        });
    }

    // --- Edit & insert / compose (Phase 10b) --------------------------------

    [Fact]
    public void InsertComposed_Rich_ResolvesTokens_ReachesInsertService_AndSavesNoFile()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            TestFiles.WriteResponse(dir.Of("lib", "A.json"), html: "<p>original</p>");
            using var svc = NewService(dir);
            var insert = new FakeInsertService();
            var vm = new LauncherViewModel(svc, insert, new AppConfig(), MorningClock);
            bool hidden = false;
            vm.RequestHide += () => hidden = true;

            // Edited, transient content still carrying a token — resolved on insert.
            vm.InsertComposed("<p>{{greeting}}! edited</p>", plain: false);

            var call = Assert.Single(insert.Calls);
            Assert.Equal("rich", call.Mode);
            Assert.Contains("Good morning", call.Item.Html);
            Assert.Contains("edited", call.Item.Html);
            Assert.DoesNotContain("{{", call.Item.Html);
            Assert.True(hidden);                                   // launcher hides after insert

            // No new file: the library still holds exactly the one seeded response, unchanged.
            using var reopened = NewService(dir);
            var only = Assert.Single(reopened.Snapshot.AllResponses);
            Assert.Equal("A", only.Name);
            Assert.Equal("<p>original</p>", only.Html);
        });
    }

    [Fact]
    public void InsertComposed_Plain_CallsInsertPlain_WithResolvedText()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            TestFiles.WriteResponse(dir.Of("lib", "A.json"));
            using var svc = NewService(dir);
            var insert = new FakeInsertService();
            var vm = new LauncherViewModel(svc, insert, new AppConfig(), MorningClock);  // Thursday

            vm.InsertComposed("<p>Day: {{day}}</p>", plain: true);

            var call = Assert.Single(insert.Calls);
            Assert.Equal("plain", call.Mode);
            Assert.Contains("Thursday", call.Item.PlainText);
            Assert.DoesNotContain("{{", call.Item.Html);
        });
    }

    [Fact]
    public void ResolveComposed_ExpandsGreetingAndIncludes_ForThePrefill()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            TestFiles.WriteResponse(dir.Of("lib", "Greeter.json"),
                html: "<p>{{greeting}}, see {{Welcome}}</p>");
            TestFiles.WriteResponse(dir.Of("lib", "Welcome.json"), html: "<p>the welcome text</p>");
            using var svc = NewService(dir);
            var vm = new LauncherViewModel(svc, new FakeInsertService(), new AppConfig(), MorningClock);

            var item = svc.Snapshot.AllResponses.Single(r => r.Name == "Greeter");
            string prefill = vm.ResolveComposed(item);

            Assert.Contains("Good morning", prefill);              // greeting filled for the editor
            Assert.Contains("the welcome text", prefill);          // include expanded
            Assert.DoesNotContain("{{", prefill);
            // The stored item is untouched.
            Assert.Equal("<p>{{greeting}}, see {{Welcome}}</p>", item.Html);
        });
    }

    [Fact]
    public void Construction_SelectsExactlyOneNavNode()
    {
        Sta(() =>
        {
            using var dir = new TempDir();
            SeedTree(dir);
            using var svc = NewService(dir);
            var vm = NewVm(svc, new FakeInsertService());

            Assert.Equal(1, Flatten(vm.NavRoots).Count(n => n.IsSelected));
            Assert.Equal(NavScope.All, vm.SelectedNav!.Scope);
        });
    }
}
