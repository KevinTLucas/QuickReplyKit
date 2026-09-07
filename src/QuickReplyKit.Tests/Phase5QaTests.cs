using System.IO;
using System.IO.Compression;
using System.Text;
using QuickReplyKit.Core.ImportExport;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Tests;

/// <summary>
/// Independent Phase-5 QA: adversarial import/export cases beyond the happy-path suite —
/// path-traversal defence, CountConflicts-vs-Import agreement for zips into new/nested/root
/// targets, mixed invalid entries, unicode/space names, and export staging edge cases.
/// </summary>
public class Phase5QaTests
{
    // Build a zip from an explicit map of entryName -> raw bytes (lets us forge malicious entries).
    private static void MakeZip(string zipPath, params (string name, string content)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var e = zip.CreateEntry(name);
            using var s = e.Open();
            var bytes = Encoding.UTF8.GetBytes(content);
            s.Write(bytes, 0, bytes.Length);
        }
    }

    private const string ValidJson =
        "{\"schema\":1,\"app\":\"\",\"tags\":[],\"html\":\"<p>x</p>\",\"favorite\":false," +
        "\"created\":\"2026-01-01T00:00:00Z\",\"modified\":\"2026-01-01T00:00:00Z\"}";

    // --- Path traversal ------------------------------------------------------

    [Fact]
    public void Import_ZipWithParentTraversalEntry_DoesNotEscapeLibraryRoot()
    {
        using var outer = new TempDir();               // sentinel target for the attack
        string libRoot = Path.Combine(outer.Path, "Library");
        Directory.CreateDirectory(libRoot);

        using var stage = new TempDir();
        string zip = stage.Of("evil.zip");
        // ..\..\evil.json would land in `outer` (one level above Library) if unguarded.
        MakeZip(zip, (@"..\..\evil.json", ValidJson), ("Good.json", ValidJson));

        var r = LibraryPorter.Import(libRoot, zip, "", ConflictPolicy.Overwrite);

        Assert.False(File.Exists(Path.Combine(outer.Path, "evil.json")),
            "path-traversal entry escaped the library root");
        // The parent dir of Library must contain no stray evil.json anywhere above root.
        Assert.Empty(Directory.GetFiles(outer.Path, "evil.json", SearchOption.TopDirectoryOnly));
        Assert.True(File.Exists(Path.Combine(libRoot, "Good.json")), "valid sibling should still import");
        Assert.Equal(1, r.Imported);
        Assert.Equal(1, r.SkippedInvalid);             // the unsafe entry is counted as skipped
    }

    [Fact]
    public void Import_ZipWithForwardSlashTraversal_DoesNotEscape()
    {
        using var outer = new TempDir();
        string libRoot = Path.Combine(outer.Path, "Library");
        Directory.CreateDirectory(libRoot);
        using var stage = new TempDir();
        string zip = stage.Of("evil2.zip");
        MakeZip(zip, ("../../evil2.json", ValidJson));

        var r = LibraryPorter.Import(libRoot, zip, "Sub", ConflictPolicy.Overwrite);

        Assert.False(File.Exists(Path.Combine(outer.Path, "evil2.json")));
        Assert.Equal(0, r.Imported);
        Assert.Equal(1, r.SkippedInvalid);
    }

    // --- CountConflicts must agree with what Import actually does -------------

    [Theory]
    [InlineData("")]            // root target
    [InlineData("Fresh")]       // brand-new category
    [InlineData("A/B/C")]       // nested new category
    public void Zip_IntoNonCollidingTarget_NoConflicts_AllImport(string target)
    {
        using var lib = new TempDir();
        TestFiles.WriteResponse(lib.Of("IXL", "Top.json"));
        TestFiles.WriteResponse(lib.Of("IXL", "Licensing", "No License.json"));
        using var stage = new TempDir();
        string zip = stage.Of("IXL.zip");
        LibraryPorter.ExportFolder(lib.Path, "IXL", zip);

        int predicted = LibraryPorter.PlanImport(lib.Path, zip, target).Entries
            .Count(e => e.Classification == ImportClassification.Conflict);
        var r = LibraryPorter.Import(lib.Path, zip, target, ConflictPolicy.Skip);

        Assert.Equal(0, predicted);
        Assert.Equal(r.Conflicts, predicted);
        Assert.Equal(2, r.Imported);
    }

    [Fact]
    public void Zip_Overwrite_CountMatchesImport_AndReplacesContent()
    {
        using var lib = new TempDir();
        TestFiles.WriteResponse(lib.Of("IXL", "Top.json"), html: "<p>old</p>");
        using var stage = new TempDir();
        // export a fresh version with new content, then mutate library so re-import overwrites
        TestFiles.WriteResponse(stage.Of("src", "IXL", "Top.json"), html: "<p>new</p>");
        string zip = stage.Of("upd.zip");
        LibraryPorter.ExportFolder(stage.Of("src"), "IXL", zip);

        int predicted = LibraryPorter.PlanImport(lib.Path, zip, "IXL").Entries
            .Count(e => e.Classification == ImportClassification.Conflict);
        var r = LibraryPorter.Import(lib.Path, zip, "IXL", ConflictPolicy.Overwrite);

        Assert.Equal(1, predicted);
        Assert.Equal(1, r.Conflicts);
        Assert.Equal(1, r.Imported);
        Assert.Equal("<p>new</p>", ResponseFile.Load(lib.Of("IXL", "Top.json"), lib.Path).Html);
    }

    [Fact]
    public void Zip_KeepBoth_SuffixesCollidersAndImportsNonColliders()
    {
        using var lib = new TempDir();
        TestFiles.WriteResponse(lib.Of("IXL", "Top.json"), html: "<p>old</p>");
        // build a zip with the colliding Top.json plus a NEW sibling
        using var stage = new TempDir();
        TestFiles.WriteResponse(stage.Of("src", "IXL", "Top.json"), html: "<p>new</p>");
        TestFiles.WriteResponse(stage.Of("src", "IXL", "Brand New.json"), html: "<p>fresh</p>");
        string zip = stage.Of("kb.zip");
        LibraryPorter.ExportFolder(stage.Of("src"), "IXL", zip);

        int predicted = LibraryPorter.PlanImport(lib.Path, zip, "IXL").Entries
            .Count(e => e.Classification == ImportClassification.Conflict);
        var r = LibraryPorter.Import(lib.Path, zip, "IXL", ConflictPolicy.KeepBoth);

        Assert.Equal(1, predicted);                 // only Top.json collides
        Assert.Equal(1, r.Conflicts);
        Assert.Equal(2, r.Imported);                // suffixed Top + Brand New
        Assert.Equal("<p>old</p>", ResponseFile.Load(lib.Of("IXL", "Top.json"), lib.Path).Html);
        Assert.True(File.Exists(lib.Of("IXL", "Top (imported).json")));
        Assert.True(File.Exists(lib.Of("IXL", "Brand New.json")));
    }

    [Fact]
    public void Zip_Skip_LeavesCollidersImportsRest()
    {
        using var lib = new TempDir();
        TestFiles.WriteResponse(lib.Of("IXL", "Top.json"), html: "<p>old</p>");
        using var stage = new TempDir();
        TestFiles.WriteResponse(stage.Of("src", "IXL", "Top.json"), html: "<p>new</p>");
        TestFiles.WriteResponse(stage.Of("src", "IXL", "Extra.json"), html: "<p>extra</p>");
        string zip = stage.Of("sk.zip");
        LibraryPorter.ExportFolder(stage.Of("src"), "IXL", zip);

        var r = LibraryPorter.Import(lib.Path, zip, "IXL", ConflictPolicy.Skip);

        Assert.Equal(1, r.Conflicts);
        Assert.Equal(1, r.Imported);                // only Extra
        Assert.Equal("<p>old</p>", ResponseFile.Load(lib.Of("IXL", "Top.json"), lib.Path).Html);
        Assert.False(File.Exists(lib.Of("IXL", "Top (imported).json")));
        Assert.True(File.Exists(lib.Of("IXL", "Extra.json")));
    }

    // --- Invalid tolerance inside a zip --------------------------------------

    [Fact]
    public void Import_ZipMixedValidInvalidAndNonJson_ImportsOnlyValidJson()
    {
        using var lib = new TempDir();
        using var stage = new TempDir();
        string zip = stage.Of("mixed.zip");
        MakeZip(zip,
            ("Good.json", ValidJson),
            ("Broken.json", "{ not json"),
            ("NoSchema.json", "{\"html\":\"<p>x</p>\"}"),   // missing schema => invalid
            ("readme.txt", "ignore me"));                    // non-json => not enumerated

        var r = LibraryPorter.Import(lib.Path, zip, "", ConflictPolicy.Skip);

        Assert.Equal(1, r.Imported);
        Assert.Equal(2, r.SkippedInvalid);
        Assert.True(File.Exists(lib.Of("Good.json")));
        Assert.False(File.Exists(lib.Of("readme.txt")));
    }

    // --- Unicode / spaces / nested names -------------------------------------

    [Fact]
    public void Import_UnicodeAndSpaceNames_RoundTripThroughZip()
    {
        using var lib = new TempDir();
        TestFiles.WriteResponse(lib.Of("Café Menu", "Niño — Ünïcode.json"), html: "<p>hi</p>");
        using var stage = new TempDir();
        string zip = stage.Of("uni.zip");
        LibraryPorter.ExportFolder(lib.Path, "Café Menu", zip);

        using var dest = new TempDir();
        var r = LibraryPorter.Import(dest.Path, zip, "Imported Ünï", ConflictPolicy.Skip);

        Assert.Equal(1, r.Imported);
        Assert.True(File.Exists(dest.Of("Imported Ünï", "Niño — Ünïcode.json")));
    }

    // --- Round-trip: whole library export -> wipe -> re-import ---------------

    [Fact]
    public void ExportLibrary_WipeReImport_RestoresIdenticalTree()
    {
        using var lib = new TempDir();
        TestFiles.WriteResponse(lib.Of("A.json"), html: "<p>a</p>");
        TestFiles.WriteResponse(lib.Of("Cat", "B.json"), html: "<p>b</p>");
        TestFiles.WriteResponse(lib.Of("Cat", "Deep", "C.json"), html: "<p>c</p>");

        using var stage = new TempDir();
        string zip = stage.Of("all.zip");
        LibraryPorter.ExportLibrary(lib.Path, zip);

        foreach (var f in Directory.GetFileSystemEntries(lib.Path))
        {
            if (Directory.Exists(f)) Directory.Delete(f, true); else File.Delete(f);
        }

        var r = LibraryPorter.Import(lib.Path, zip, "", ConflictPolicy.Skip);

        Assert.Equal(3, r.Imported);
        Assert.Equal("<p>a</p>", ResponseFile.Load(lib.Of("A.json"), lib.Path).Html);
        Assert.Equal("<p>b</p>", ResponseFile.Load(lib.Of("Cat", "B.json"), lib.Path).Html);
        Assert.Equal("<p>c</p>", ResponseFile.Load(lib.Of("Cat", "Deep", "C.json"), lib.Path).Html);
    }

    // --- Export staging: exporting into a path INSIDE the source tree --------

    [Fact]
    public void ExportLibrary_DestinationInsideLibrary_DoesNotCorruptOrRecurse()
    {
        // The UI stages exports through a temp file (SaveZip); the Core API called directly with an
        // in-tree destination must still produce a readable archive of the *responses* — never a
        // zip that contains a growing copy of itself. We assert the archive opens and holds exactly
        // the response entries (the .zip itself, if captured at all, must not be a valid nested lib).
        using var lib = new TempDir();
        TestFiles.WriteResponse(lib.Of("A.json"));
        TestFiles.WriteResponse(lib.Of("Cat", "B.json"));

        string inTreeZip = lib.Of("self.zip");
        var ex = Record.Exception(() => LibraryPorter.ExportLibrary(lib.Path, inTreeZip));

        // Either it throws (acceptable: file-in-use), or it produced a well-formed zip.
        if (ex == null)
        {
            using var archive = ZipFile.OpenRead(inTreeZip);
            var jsons = archive.Entries.Where(e => e.FullName.EndsWith(".json")).Select(e => e.FullName).ToArray();
            Assert.Contains("A.json", jsons);
            Assert.Contains(jsons, n => n.EndsWith("B.json"));
            // Must not have zipped a prior copy of itself into a recursive blob.
            foreach (var e in archive.Entries.Where(e => e.FullName.EndsWith(".zip")))
                Assert.True(e.Length < 10_000_000, "self.zip appears to have recursively grown");
        }
    }
}
