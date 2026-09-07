using System.IO;
using System.IO.Compression;
using QuickReplyKit.ViewModels;

namespace QuickReplyKit.Tests;

public class ImportPreviewTests
{
    [Fact]
    public void Analyze_SingleFile_NewWhenAbsent_IdenticalWhenSame_ConflictWhenDifferent()
    {
        using var lib = new TempDir();
        using var stage = new TempDir();

        // New: nothing at the target yet.
        TestFiles.WriteResponse(stage.Of("Hi.json"), html: "<p>v1</p>");
        var e1 = ImportPreview.Analyze(lib.Path, stage.Of("Hi.json"), "Support");
        Assert.Single(e1);
        Assert.Equal(ImportKind.New, e1[0].Kind);
        Assert.Equal("Support/Hi.json", e1[0].Dest);

        // Put an identical copy at the destination → Identical.
        Directory.CreateDirectory(Path.Combine(lib.Path, "Support"));
        File.Copy(stage.Of("Hi.json"), Path.Combine(lib.Path, "Support", "Hi.json"));
        Assert.Equal(ImportKind.Identical, ImportPreview.Analyze(lib.Path, stage.Of("Hi.json"), "Support")[0].Kind);

        // Change the incoming content → Conflict (same path, different bytes).
        TestFiles.WriteResponse(stage.Of("Hi.json"), html: "<p>v2 different</p>");
        Assert.Equal(ImportKind.Conflict, ImportPreview.Analyze(lib.Path, stage.Of("Hi.json"), "Support")[0].Kind);
    }

    [Fact]
    public void Analyze_Zip_ClassifiesEachEntry_AndCounts()
    {
        using var lib = new TempDir();
        using var stage = new TempDir();

        // Library already has IXL/A.json (identical to what we'll import) and IXL/B.json (different).
        TestFiles.WriteResponse(Path.Combine(lib.Path, "IXL", "A.json"), html: "<p>same</p>");
        TestFiles.WriteResponse(Path.Combine(lib.Path, "IXL", "B.json"), html: "<p>old</p>");

        // Build a zip whose entries land under IXL: A (identical), B (changed), C (new).
        var src = stage.Of("src");
        TestFiles.WriteResponse(Path.Combine(src, "A.json"), html: "<p>same</p>");
        TestFiles.WriteResponse(Path.Combine(src, "B.json"), html: "<p>new content</p>");
        TestFiles.WriteResponse(Path.Combine(src, "C.json"), html: "<p>brand new</p>");
        var zip = stage.Of("bundle.zip");
        ZipFile.CreateFromDirectory(src, zip);

        var entries = ImportPreview.Analyze(lib.Path, zip, "IXL");
        Assert.Equal(1, ImportPreview.Count(entries, ImportKind.Identical));
        Assert.Equal(1, ImportPreview.Count(entries, ImportKind.Conflict));
        Assert.Equal(1, ImportPreview.Count(entries, ImportKind.New));
    }
}
