using System.IO;
using System.Threading;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Tests;

public class LibraryServiceTests
{
    private static LibraryService NewService(TempDir dir) =>
        new(dir.Of("lib"), seedSourceDir: null, recentPath: dir.Of("recent.json"), watch: false);

    [Fact]
    public void Updated_Fires_AndRebuildsSnapshot_WhenAFileIsAdded()
    {
        using var dir = new TempDir();
        TestFiles.WriteResponse(dir.Of("lib", "A.json"));

        using var svc = new LibraryService(dir.Of("lib"), seedSourceDir: null,
            recentPath: dir.Of("recent.json"), watch: true);
        Assert.Single(svc.Snapshot.AllResponses);

        var fired = new ManualResetEventSlim();
        svc.Updated += () => fired.Set();

        TestFiles.WriteResponse(dir.Of("lib", "B.json"));

        Assert.True(fired.Wait(5000), "LibraryService.Updated never fired for a new file");
        // The snapshot is rebuilt before Updated is raised.
        Assert.Contains(svc.Snapshot.AllResponses, r => r.Name == "B");
    }

    [Fact]
    public void Repoint_RescansTheNewRoot()
    {
        using var dir = new TempDir();
        TestFiles.WriteResponse(dir.Of("libA", "OnlyInA.json"));
        TestFiles.WriteResponse(dir.Of("libB", "OnlyInB.json"));

        using var svc = new LibraryService(dir.Of("libA"), seedSourceDir: null,
            recentPath: dir.Of("recent.json"), watch: false);
        Assert.Contains(svc.Snapshot.AllResponses, r => r.Name == "OnlyInA");

        svc.Repoint(dir.Of("libB"));

        Assert.Contains(svc.Snapshot.AllResponses, r => r.Name == "OnlyInB");
        Assert.DoesNotContain(svc.Snapshot.AllResponses, r => r.Name == "OnlyInA");
    }

    [Fact]
    public void Repoint_ToSameRoot_JustRescans()
    {
        using var dir = new TempDir();
        TestFiles.WriteResponse(dir.Of("lib", "A.json"));
        using var svc = NewService(dir);
        Assert.Single(svc.Snapshot.AllResponses);

        TestFiles.WriteResponse(dir.Of("lib", "B.json"));   // added out-of-band (no watcher)
        svc.Repoint(dir.Of("lib"));                          // same path → rescan picks it up

        Assert.Equal(2, svc.Snapshot.AllResponses.Count);
    }

    [Fact]
    public void Search_DelegatesToRankedSearch_ExcludingInvalid()
    {
        using var dir = new TempDir();
        TestFiles.WriteResponse(dir.Of("lib", "Password Reset.json"), html: "<p>reset your password</p>");
        TestFiles.WriteResponse(dir.Of("lib", "Ticket.json"), html: "<p>please reset your pass now</p>");
        File.WriteAllText(dir.Of("lib", "Broken.json"), "{ not json");

        using var svc = NewService(dir);
        var results = svc.Search("pass");

        // Name-prefix ("Password") ranks above body match ("Ticket"); broken file (name has no
        // "pass") is not matched. Search now returns SearchHits, so read the item off each hit.
        Assert.Equal(new[] { "Password Reset", "Ticket" }, results.Select(r => r.Item.Name));
    }

    [Fact]
    public void ToggleFavorite_PersistsToDisk_AndReflectsInFavorites()
    {
        using var dir = new TempDir();
        TestFiles.WriteResponse(dir.Of("lib", "A.json"), favorite: false);

        using var svc = NewService(dir);
        var item = svc.Snapshot.AllResponses.Single();
        Assert.False(item.Favorite);

        svc.ToggleFavorite(item);

        Assert.True(item.Favorite);                                   // reflected in memory
        Assert.Contains(svc.Favorites(), r => r.Name == "A");        // reflected in Favorites()

        // Persisted: a fresh service reading the same folder sees it favorited.
        using var reopened = NewService(dir);
        Assert.True(reopened.Snapshot.AllResponses.Single().Favorite);
        Assert.Contains(reopened.Favorites(), r => r.Name == "A");

        svc.ToggleFavorite(item);
        Assert.False(item.Favorite);
        Assert.DoesNotContain(svc.Favorites(), r => r.Name == "A");
    }

    [Fact]
    public void Recent_ReturnsMostRecentFirst_AndDeduplicates()
    {
        using var dir = new TempDir();
        TestFiles.WriteResponse(dir.Of("lib", "A.json"));
        TestFiles.WriteResponse(dir.Of("lib", "B.json"));

        using var svc = NewService(dir);
        var a = svc.Snapshot.AllResponses.Single(r => r.Name == "A");
        var b = svc.Snapshot.AllResponses.Single(r => r.Name == "B");

        svc.MarkUsed(a);
        svc.MarkUsed(b);
        Assert.Equal(new[] { "B", "A" }, svc.Recent().Select(r => r.Name));

        svc.MarkUsed(a);   // re-use moves A to the front, no duplicate
        Assert.Equal(new[] { "A", "B" }, svc.Recent().Select(r => r.Name));
    }

    [Fact]
    public void Recent_PersistsAcrossReopen_AndSkipsMissingItems()
    {
        using var dir = new TempDir();
        TestFiles.WriteResponse(dir.Of("lib", "A.json"));
        TestFiles.WriteResponse(dir.Of("lib", "Gone.json"));

        using (var svc = NewService(dir))
        {
            svc.MarkUsed(svc.Snapshot.AllResponses.Single(r => r.Name == "Gone"));
            svc.MarkUsed(svc.Snapshot.AllResponses.Single(r => r.Name == "A"));
        }

        File.Delete(dir.Of("lib", "Gone.json"));

        using var reopened = NewService(dir);
        // "Gone" is remembered in recent.json but no longer in the library → skipped.
        Assert.Equal(new[] { "A" }, reopened.Recent().Select(r => r.Name));
    }

    [Fact]
    public void FindByName_IsCaseInsensitive_AndSkipsInvalid()
    {
        using var dir = new TempDir();
        TestFiles.WriteResponse(dir.Of("lib", "Sub", "Welcome.json"), html: "<p>hi</p>");
        File.WriteAllText(dir.Of("lib", "Broken.json"), "{ not json");
        using var svc = NewService(dir);

        Assert.Equal("Welcome", svc.FindByName("welcome")?.Name);   // case-insensitive, any folder
        Assert.Equal("<p>hi</p>", svc.FindByName("WELCOME")?.Html);
        Assert.Null(svc.FindByName("Broken"));                       // invalid files never match
        Assert.Null(svc.FindByName("Nope"));                         // unknown name → null
    }

    [Fact]
    public void DeferInitialScan_StartsEmptyAndLoading_ThenLoadInitialPopulatesAndFiresUpdated()
    {
        using var dir = new TempDir();
        TestFiles.WriteResponse(dir.Of("lib", "A.json"));
        TestFiles.WriteResponse(dir.Of("lib", "Sub", "B.json"));

        using var svc = new LibraryService(dir.Of("lib"), seedSourceDir: null,
            recentPath: dir.Of("recent.json"), watch: false, deferInitialScan: true);

        // Before the deferred scan runs: empty snapshot, IsLoading true (drives the "Loading…" UI).
        Assert.True(svc.IsLoading);
        Assert.Empty(svc.Snapshot.AllResponses);

        var fired = new ManualResetEventSlim();
        svc.Updated += () => fired.Set();

        svc.LoadInitial();   // what App runs on a background Task at startup

        Assert.True(fired.Wait(5000), "LoadInitial did not raise Updated");
        Assert.False(svc.IsLoading);
        Assert.Equal(2, svc.Snapshot.AllResponses.Count);
        Assert.Contains(svc.Snapshot.AllResponses, r => r.Name == "A");
        Assert.Contains(svc.Snapshot.AllResponses, r => r.Name == "B");
    }

    [Fact]
    public void Reload_PicksUpNewFiles()
    {
        using var dir = new TempDir();
        TestFiles.WriteResponse(dir.Of("lib", "A.json"));
        using var svc = NewService(dir);
        Assert.Single(svc.Snapshot.AllResponses);

        TestFiles.WriteResponse(dir.Of("lib", "Sub", "B.json"));
        svc.Reload();

        Assert.Equal(2, svc.Snapshot.AllResponses.Count);
    }
}
