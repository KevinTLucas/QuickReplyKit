using System.IO;
using QuickReplyKit.Core.ImportExport;

namespace QuickReplyKit.Tests;

public class LibraryBackupTests
{
    [Fact]
    public void Create_EmptyOrMissingLibrary_ReturnsNull()
    {
        using var t = new TempDir();
        var lib = t.Of("lib");        // never created → missing
        Assert.Null(LibraryBackup.Create(lib, t.Of("backups")));
        Directory.CreateDirectory(lib);   // exists but empty
        Assert.Null(LibraryBackup.Create(lib, t.Of("backups")));
    }

    [Fact]
    public void Create_SnapshotsLibrary_AndListsNewestFirst()
    {
        using var t = new TempDir();
        var lib = t.Of("lib");
        TestFiles.WriteResponse(Path.Combine(lib, "IXL", "Hi.json"));
        var backups = t.Of("backups");

        var z1 = LibraryBackup.Create(lib, backups);
        System.Threading.Thread.Sleep(1100);   // distinct second-resolution timestamp
        var z2 = LibraryBackup.Create(lib, backups);

        Assert.NotNull(z1); Assert.NotNull(z2);
        Assert.True(File.Exists(z2!));
        var list = LibraryBackup.List(backups);
        Assert.Equal(2, list.Count);
        Assert.Equal(z2, list[0].Path);   // newest first
        Assert.True(list[0].Bytes > 0);
    }

    [Fact]
    public void Create_PrunesToMaxKeep()
    {
        using var t = new TempDir();
        var lib = t.Of("lib");
        TestFiles.WriteResponse(Path.Combine(lib, "a.json"));
        var backups = t.Of("backups");
        // Force > MaxKeep distinct snapshots by writing timestamped names directly is slow; instead
        // create MaxKeep+3 real zips (they collide within a second → -n suffixes keep them distinct).
        for (int i = 0; i < LibraryBackup.MaxKeep + 3; i++) LibraryBackup.Create(lib, backups);
        Assert.True(LibraryBackup.List(backups).Count <= LibraryBackup.MaxKeep);
    }

    [Fact]
    public void Restore_ReplacesLibrary_AndSnapshotsCurrentFirst()
    {
        using var t = new TempDir();
        var lib = t.Of("lib");
        var backups = t.Of("backups");

        // v1: one response → snapshot it.
        TestFiles.WriteResponse(Path.Combine(lib, "Original.json"), html: "<p>v1</p>");
        var snap = LibraryBackup.Create(lib, backups);
        Assert.NotNull(snap);

        // Mutate the library: delete the original, add a different one.
        File.Delete(Path.Combine(lib, "Original.json"));
        TestFiles.WriteResponse(Path.Combine(lib, "Changed.json"), html: "<p>v2</p>");

        int before = LibraryBackup.List(backups).Count;
        LibraryBackup.Restore(snap!, lib, backups);

        // Library is back to v1 content, and the current (v2) state was snapshotted as a safety net.
        Assert.True(File.Exists(Path.Combine(lib, "Original.json")));
        Assert.False(File.Exists(Path.Combine(lib, "Changed.json")));
        Assert.True(LibraryBackup.List(backups).Count > before);
    }

    [Fact]
    public void Restore_MissingBackup_Throws()
    {
        using var t = new TempDir();
        Assert.Throws<FileNotFoundException>(() =>
            LibraryBackup.Restore(t.Of("nope.zip"), t.Of("lib"), t.Of("backups")));
    }
}
