using System.IO;
using System.IO.Compression;
using QuickReplyKit.Core.ImportExport;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Tests;

public class LibraryPorterTests
{
    [Fact]
    public void Export_SingleJson_ThenImport_RoundTripsEqual()
    {
        using var src = new TempDir();
        using var dst = new TempDir();
        TestFiles.WriteResponse(src.Of("Greeting.json"), html: "<p>hello</p>", app: "Outlook", tags: new[] { "hi" });

        string exported = dst.Of("out", "Greeting.json");
        var item = ResponseFile.Load(src.Of("Greeting.json"), src.Path);
        LibraryPorter.Export(item, exported);

        using var lib = new TempDir();
        var r = LibraryPorter.Import(lib.Path, exported, "Inbox", ConflictPolicy.Skip);

        Assert.Equal(1, r.Imported);
        var loaded = ResponseFile.Load(lib.Of("Inbox", "Greeting.json"), lib.Path);
        Assert.True(loaded.IsValid);
        Assert.Equal("<p>hello</p>", loaded.Html);
        Assert.Equal("Outlook", loaded.App);
        Assert.Equal(new[] { "hi" }, loaded.Tags);
    }

    [Fact]
    public void ExportFolder_PreservesHierarchyInZip()
    {
        using var lib = new TempDir();
        TestFiles.WriteResponse(lib.Of("IXL", "Top.json"));
        TestFiles.WriteResponse(lib.Of("IXL", "Licensing", "No License.json"));

        string zip = lib.Of("IXL.zip");
        LibraryPorter.ExportFolder(lib.Path, "IXL", zip);

        using var archive = ZipFile.OpenRead(zip);
        var names = archive.Entries.Select(e => e.FullName).Where(n => n.EndsWith(".json")).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "Licensing/No License.json", "Top.json" }, names);
    }

    [Fact]
    public void ExportFolder_Import_RebuildsSubtreeUnderTargetCategory()
    {
        using var lib = new TempDir();
        TestFiles.WriteResponse(lib.Of("IXL", "Top.json"));
        TestFiles.WriteResponse(lib.Of("IXL", "Licensing", "No License.json"));
        string zip = lib.Of("IXL.zip");
        LibraryPorter.ExportFolder(lib.Path, "IXL", zip);

        using var dest = new TempDir();
        var r = LibraryPorter.Import(dest.Path, zip, "Imported", ConflictPolicy.Skip);

        Assert.Equal(2, r.Imported);
        Assert.True(File.Exists(dest.Of("Imported", "Top.json")));
        Assert.True(File.Exists(dest.Of("Imported", "Licensing", "No License.json")));
    }

    [Fact]
    public void ExportLibrary_Import_RoundTrips()
    {
        using var lib = new TempDir();
        TestFiles.WriteResponse(lib.Of("A.json"));
        TestFiles.WriteResponse(lib.Of("Cat", "B.json"));
        // stage the zip outside the library so it is not zipped into itself
        using var stage = new TempDir();
        string zip = stage.Of("all.zip");
        LibraryPorter.ExportLibrary(lib.Path, zip);

        using var dest = new TempDir();
        var r = LibraryPorter.Import(dest.Path, zip, "", ConflictPolicy.Skip);

        Assert.Equal(2, r.Imported);
        Assert.True(File.Exists(dest.Of("A.json")));
        Assert.True(File.Exists(dest.Of("Cat", "B.json")));
    }

    [Fact]
    public void Import_ZipWithOneBrokenJson_ImportsValidOnes_CountsSkipped()
    {
        using var stage = new TempDir();
        TestFiles.WriteResponse(stage.Of("z", "Good.json"));
        Directory.CreateDirectory(stage.Of("z"));
        File.WriteAllText(stage.Of("z", "Broken.json"), "{ not valid json ");
        string zip = stage.Of("mixed.zip");
        ZipFile.CreateFromDirectory(stage.Of("z"), zip);

        using var dest = new TempDir();
        var r = LibraryPorter.Import(dest.Path, zip, "", ConflictPolicy.Skip);

        Assert.Equal(1, r.Imported);
        Assert.Equal(1, r.SkippedInvalid);
        Assert.True(File.Exists(dest.Of("Good.json")));
        Assert.False(File.Exists(dest.Of("Broken.json")));
    }

    [Fact]
    public void Import_Conflict_Overwrite_ReplacesOriginal()
    {
        using var stage = new TempDir();
        TestFiles.WriteResponse(stage.Of("New.json"), html: "<p>new</p>");
        using var dest = new TempDir();
        TestFiles.WriteResponse(dest.Of("New.json"), html: "<p>old</p>");

        var r = LibraryPorter.Import(dest.Path, stage.Of("New.json"), "", ConflictPolicy.Overwrite);

        Assert.Equal(1, r.Imported);
        Assert.Equal(1, r.Conflicts);
        Assert.Equal("<p>new</p>", ResponseFile.Load(dest.Of("New.json"), dest.Path).Html);
    }

    [Fact]
    public void Import_Conflict_KeepBoth_CreatesSuffixedCopy()
    {
        using var stage = new TempDir();
        TestFiles.WriteResponse(stage.Of("New.json"), html: "<p>new</p>");
        using var dest = new TempDir();
        TestFiles.WriteResponse(dest.Of("New.json"), html: "<p>old</p>");

        var r = LibraryPorter.Import(dest.Path, stage.Of("New.json"), "", ConflictPolicy.KeepBoth);

        Assert.Equal(1, r.Imported);
        Assert.Equal(1, r.Conflicts);
        Assert.Equal("<p>old</p>", ResponseFile.Load(dest.Of("New.json"), dest.Path).Html);
        Assert.True(File.Exists(dest.Of("New (imported).json")));
        Assert.Equal("<p>new</p>", ResponseFile.Load(dest.Of("New (imported).json"), dest.Path).Html);
    }

    [Fact]
    public void Import_Conflict_KeepBoth_Twice_IncrementsSuffix()
    {
        using var stage = new TempDir();
        TestFiles.WriteResponse(stage.Of("New.json"), html: "<p>new</p>");
        using var dest = new TempDir();
        TestFiles.WriteResponse(dest.Of("New.json"), html: "<p>old</p>");

        LibraryPorter.Import(dest.Path, stage.Of("New.json"), "", ConflictPolicy.KeepBoth);
        LibraryPorter.Import(dest.Path, stage.Of("New.json"), "", ConflictPolicy.KeepBoth);

        Assert.True(File.Exists(dest.Of("New (imported).json")));
        Assert.True(File.Exists(dest.Of("New (imported 2).json")));
    }

    [Fact]
    public void Import_Conflict_Skip_LeavesOriginal()
    {
        using var stage = new TempDir();
        TestFiles.WriteResponse(stage.Of("New.json"), html: "<p>new</p>");
        using var dest = new TempDir();
        TestFiles.WriteResponse(dest.Of("New.json"), html: "<p>old</p>");

        var r = LibraryPorter.Import(dest.Path, stage.Of("New.json"), "", ConflictPolicy.Skip);

        Assert.Equal(0, r.Imported);
        Assert.Equal(1, r.Conflicts);
        Assert.Equal("<p>old</p>", ResponseFile.Load(dest.Of("New.json"), dest.Path).Html);
    }

    [Fact]
    public void Import_SingleJson_IntoChosenCategory()
    {
        using var stage = new TempDir();
        TestFiles.WriteResponse(stage.Of("Hi.json"));
        using var dest = new TempDir();

        var r = LibraryPorter.Import(dest.Path, stage.Of("Hi.json"), "Support/General", ConflictPolicy.Skip);

        Assert.Equal(1, r.Imported);
        Assert.True(File.Exists(dest.Of("Support", "General", "Hi.json")));
    }
}
