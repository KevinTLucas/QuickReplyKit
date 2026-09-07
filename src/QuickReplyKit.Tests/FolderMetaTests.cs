using System.IO;
using QuickReplyKit.Core.Library;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Tests;

public class FolderMetaTests
{
    [Fact]
    public void SaveThenLoad_RoundTrips_AndDedupes()
    {
        using var t = new TempDir();
        FolderMeta.Save(t.Path, new[] { "IXL", "ixl", " math ", "" });
        var tags = FolderMeta.Load(t.Path);
        Assert.Equal(new[] { "IXL", "math" }, tags);   // deduped case-insensitively, trimmed, blanks dropped
    }

    [Fact]
    public void Save_EmptyList_RemovesSidecar()
    {
        using var t = new TempDir();
        FolderMeta.Save(t.Path, new[] { "a" });
        Assert.True(File.Exists(Path.Combine(t.Path, FolderMeta.FileName)));
        FolderMeta.Save(t.Path, System.Array.Empty<string>());
        Assert.False(File.Exists(Path.Combine(t.Path, FolderMeta.FileName)));
    }

    [Fact]
    public void Load_NoSidecar_ReturnsEmpty()
    {
        using var t = new TempDir();
        Assert.Empty(FolderMeta.Load(t.Path));
    }

    [Fact]
    public void Scanner_SkipsSidecar_ButExposesFolderTags()
    {
        using var t = new TempDir();
        var folder = t.Of("IXL");
        TestFiles.WriteResponse(Path.Combine(folder, "Hi.json"));
        FolderMeta.Save(folder, new[] { "priority", "k12" });

        var snap = LibraryScanner.Scan(t.Path);
        // The sidecar is NOT a response (would otherwise show as an invalid ⚠ item).
        Assert.Single(snap.AllResponses);
        Assert.Equal("Hi", snap.AllResponses[0].Name);
        Assert.True(snap.AllResponses[0].IsValid);
        // Folder tags surface on the node.
        var ixl = snap.Root.Children.Single(c => c.Name == "IXL");
        Assert.Equal(new[] { "priority", "k12" }, ixl.Tags);
    }

    [Fact]
    public void ApplyTags_UnionsIntoResponses_AndCountsOnlyChanged()
    {
        using var t = new TempDir();
        var folder = t.Of("IXL");
        TestFiles.WriteResponse(Path.Combine(folder, "A.json"), tags: new[] { "existing" });
        TestFiles.WriteResponse(Path.Combine(folder, "B.json"), tags: new[] { "priority" });
        var snap = LibraryScanner.Scan(t.Path);
        var responses = snap.AllResponses;

        int changed = FolderMeta.ApplyTags(responses, new[] { "priority", "k12" }, t.Path);
        Assert.Equal(2, changed);   // A gains priority+k12, B gains k12 → both change

        // Re-applying the same tags is a no-op (already present).
        var rescan = LibraryScanner.Scan(t.Path).AllResponses;
        Assert.Equal(0, FolderMeta.ApplyTags(rescan, new[] { "priority", "k12" }, t.Path));
        // Union preserved existing tags.
        var a = rescan.Single(r => r.Name == "A");
        Assert.Contains("existing", a.Tags);
        Assert.Contains("k12", a.Tags);
    }
}
